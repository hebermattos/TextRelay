# AGENTS.md

This file applies to the entire repository. Follow it for every change unless a more specific `AGENTS.md` exists in a subdirectory.

## Project goal

Build a secure multi-tenant REST API for sending, receiving, tracking, and querying SMS messages. Twilio and Bandwidth are the initial providers, but provider-specific behavior must remain behind common application contracts so additional providers can be added without changing core use cases.

## Technology and structure

- Use C# and .NET 8 with ASP.NET Core controllers.
- Use PostgreSQL and Dapper. Do not introduce Entity Framework.
- Preserve the current dependency direction:
  - `TextRelay.Domain`: domain models and enums with no infrastructure dependencies.
  - `TextRelay.Application`: use cases and provider-independent interfaces.
  - `TextRelay.Infrastructure`: Dapper repositories, encryption, observability, and provider integrations.
  - `TextRelay.Api`: HTTP endpoints, authentication, authorization, middleware, and API composition root.
  - `TextRelay.Worker`: independently deployable background-process host for RabbitMQ consumers, queue publishing, scheduling, retries, alerts, and messaging monitoring.
- Organize HTTP and application code by vertical slice. Put feature-specific controllers and HTTP collaborators under `TextRelay.Api/Features/<Feature>` and matching use cases/contracts under `TextRelay.Application/Features/<Feature>`. Keep controllers thin; business rules belong in application services, while external API and database details belong in infrastructure.\n- Keep truly cross-cutting concerns outside feature slices (for example middleware, rate limiting, health checks, OpenAPI, common application abstractions, and shared security abstractions).
- Use asynchronous APIs for HTTP and database I/O and propagate `CancellationToken`.
- Keep the implementation as simple and readable as possible. Avoid unnecessary abstractions and complexity.

## Messaging and background processing

- Keep HTTP request handling in `TextRelay.Api` and background execution in `TextRelay.Worker`; the API and Worker must remain independently deployable and scalable.
- RabbitMQ messages for SMS sending carry message identity, not the full SMS payload. Consumers load the current message from PostgreSQL before sending.
- Publish an SMS send event only when the persisted `SmsQueueStatus` is `Queued`.
- Before calling an SMS provider, the consumer must atomically claim the persisted message from `Queued` to `Processing`. If the claim fails, do not send. This is the primary concurrency/idempotency guard against duplicate sends.
- Keep transport state (`SmsQueueStatus`) separate from provider/delivery state (`SmsStatus`). Do not add queue lifecycle values such as `Queued` or `Scheduled` to `SmsStatus`.
- Scheduled messages remain persisted in UTC until due, then transition to `Queued` through an atomic database operation before publication.
- RabbitMQ delivery is at-least-once. Consumers and callbacks must therefore remain idempotent; never rely on a broker message being delivered exactly once.
- Keep provider calls outside database transactions. Use persisted state transitions and retryable background processing rather than attempting a distributed transaction between PostgreSQL and RabbitMQ.
- Retry only transient provider failures (HTTP 429, HTTP 5xx, and provider network failures). Use MassTransit delayed redelivery backed by RabbitMQ's delayed-message exchange so retry delays survive Worker restarts and do not occupy consumer slots.
- A delayed redelivery may continue a message already atomically claimed as `Processing` only when MassTransit redelivery metadata proves it belongs to that delivery chain. Unrelated duplicate deliveries must still fail the claim and must not call the provider.
- Keep the SMS retry policy platform-wide and configurable through `SmsRetry:MaxAttempts` and `SmsRetry:InitialIntervalSeconds`; do not introduce tenant-specific retry behavior without an explicit requirement.
- Background polling must be bounded and configurable where appropriate; avoid tight polling loops and unnecessary database scans.
- Keep RabbitMQ-specific transport concerns out of core application use cases.

## SMS providers

- Every provider must implement `ISmsProvider` and be resolved through `ISmsProviderResolver`.
- Never branch on provider names in controllers or core use cases when polymorphism can handle the behavior.
- Provider credentials and configuration are tenant-specific.
- Twilio inbound and status callbacks must validate `X-Twilio-Signature` using the configured public HTTPS callback URL.
- Bandwidth authentication uses OAuth 2.0 Client Credentials.
- A tenant must not override a sender number that is not configured for that tenant.
- Webhooks must be idempotent and must resolve the owning tenant from validated provider data, never from an untrusted tenant identifier supplied by the caller.

## Multi-tenancy and authentication

- Protected endpoints use JWT bearer authentication.
- The authenticated `tenant_id` claim is the only source of tenant identity for client-facing operations.
- Do not accept a tenant ID from route, query, header, or body when the tenant can be derived from the JWT.
- Every tenant-owned query and mutation must filter by `TenantId` at the database boundary.
- Never expose records, message history, provider settings, or logs belonging to another tenant.
- API client secrets must remain PBKDF2-SHA256 hashes with unique random salts. Return a generated secret only once during provisioning.
- Platform administrators authenticate with individual usernames and passwords. Store only PBKDF2-SHA256 password hashes (600,000 iterations) with random salts; recheck administrator activation on authenticated requests. The provisioning key is only for the legacy tenant-bootstrap endpoint, never UI login.

## Security and sensitive data

- Encrypt provider secrets and sensitive provider settings in the application with AES-256-GCM before database persistence.
- Encrypt SMS sender, recipient, and body in the application before database persistence. Decrypt them only after a tenant-scoped query succeeds.
- Keep the Base64 master key outside PostgreSQL and outside the repository. Plaintext secrets may exist only in memory while required.
- Never commit credentials, tokens, production connection strings, encryption keys, or real customer data.
- Use fixed-time comparison for secrets and webhook signatures where applicable.
- Do not return provider error bodies directly to API clients.
- Do not log SMS bodies, authorization headers, credentials, access tokens, secrets, or phone numbers.
- Validate inputs, bound pagination, and avoid disclosing whether resources exist in another tenant.
- Anonymous endpoints are permitted only for authentication/bootstrap requirements or validated provider webhooks.

## Database rules

- Do not create or use migrations. Always treat the target database as new.
- `database/schema.sql` is the complete canonical schema for the application database.
- `database/logs-schema.sql` is the complete canonical schema for the audit/error-log database. Technical OpenTelemetry telemetry is stored separately in ClickStack/ClickHouse.
- Update the appropriate complete schema whenever persistence changes.
- Keep the example tenant seed under `database/seeds/` and use it only for local/test bootstrap.
- Use parameterized Dapper queries. Never build SQL from untrusted values.
- Prefer explicit columns and enforce important invariants with SQL constraints and indexes.

## Observability and customer-visible logs

- Use `ILogger<T>` and OpenTelemetry for structured logs, traces, and metrics.
- Store tenant user activity and error-level platform logs in the separate database configured by `ConnectionStrings__LogsPostgres`; do not mix them with SMS application tables.
- Export technical OpenTelemetry logs, traces, and metrics over OTLP to ClickStack/ClickHouse. Do not persist traces or metrics in PostgreSQL.
- Use `textrelay-api` as the API OpenTelemetry service name and `textrelay-worker` as the Worker service name. Give each running process a distinct `service.instance.id`.
- Preserve distributed tracing semantics for SMS processing: queue publication is a `Producer` span, queue consumption is a `Consumer` span, the atomic database claim is an `Internal` span, and the provider call is a `Client` span.
- Mark the span at the failing operation as `Error` and record exception information only when it is safe. Never attach provider response bodies or other potentially sensitive payloads to spans.
- Include safe correlation fields in traces where useful: `TenantId`, `MessageId`, `Provider`, `TraceId`, and `SpanId`.
- Keep metric dimensions low-cardinality. Do not use `TenantId`, `MessageId`, phone numbers, or other per-request/per-customer identifiers as metric labels. Prefer bounded dimensions such as provider, status, and queue name.
- Record latency measurements on both successful and failed operations so failure latency is observable.
- Never emit SMS bodies, phone numbers, credentials, access tokens, authorization headers, or secrets in logs, traces, metrics, exception attributes, or OpenTelemetry resource attributes.
- Customer log access must be authenticated and filtered by the JWT tenant claim.
- Tenant-less technical events are support-only and must not be returned by customer-facing endpoints.
- Preserve the separation between technical observability, customer audit events, and SMS message/status history.

## Docker and configuration

- Docker Compose is for local and automated tests only, not the production deployment model.
- Compose must create each database and apply its complete schema only when the database does not exist, provision the example tenant and provider fixtures on first initialization, and preserve data across ordinary restarts. Use `docker compose down --remove-orphans --volumes` only for an intentional clean reset.
- Keep development fallback secrets clearly non-production and documented.
- Use ASP.NET Core configuration and environment variables for deployment-specific values.
- `Sms:PublicBaseUrl` must remain an externally reachable absolute HTTPS URL and must not be derived from untrusted proxy headers.

## Tests and quality gate

- Add or update tests for every behavior change and bug fix.
- Maintain at least 80% line coverage as measured by the repository Coverlet settings.
- Test tenant isolation, authorization failures, invalid input, provider failures, webhook signature validation, idempotency, and secret-handling paths when relevant.
- Do not weaken coverage exclusions or assertions merely to make CI pass.
- Run before opening a PR:

```bash
dotnet restore TextRelay.Api.sln
dotnet build TextRelay.Api.sln --configuration Release --no-restore
dotnet test TextRelay.Api.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

- When persistence behavior changes, also validate it against PostgreSQL; unit tests alone are not sufficient for Dapper SQL correctness.

## Codex IDE restrictions

When Codex is being used through an IDE integration:

- Do not run tests.
- Do not create commits.
- Leave changes uncommitted for the user to review and handle. These restrictions override repository workflow instructions that would otherwise require Codex to run tests or create commits.

## Change and PR discipline

- Keep each PR focused on one cohesive functionality or correction. Do not combine unrelated features.
- Use a feature/fix branch; never commit directly to `main`.
- Update `README.md` and Docker test configuration when behavior, configuration, API surface, or bootstrap requirements change.
- Create a pull request for each completed change and merge it into `main`.
- Do not run CI for feature branches or pull requests. CI runs only on `main` after the pull request is merged.
- Prefer squash merge so `main` retains one focused commit per PR.
- Preserve existing user changes and avoid unrelated refactors.
- Complete every authorized repository change through the full workflow: create a focused branch, implement and validate the change locally when permitted, open a pull request, and merge it into `main`. Verify the resulting CI run on `main` after the merge.
- Each pull request must contain enough related work to represent a meaningful, cohesive change, while never combining unrelated features or fixes.
- When a requirement is unclear, incomplete, contradictory, or permits materially different implementations, ask the user for clarification before proceeding.

## Current known gaps

- MFA and self-service password recovery are not implemented yet.

Treat these as separate future PRs. Do not silently implement them as part of an unrelated task.
