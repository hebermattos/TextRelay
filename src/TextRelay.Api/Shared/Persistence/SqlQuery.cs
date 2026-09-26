using System.Collections.Concurrent;
using System.Reflection;

namespace Sms.Infrastructure.Sql;

public static class SqlQuery
{
    private const string ResourcePrefix = "TextRelay.Api.";
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> PersistenceOwners = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AdministrationRepository"] = "Features.Administration.Sql",
        ["AlertRepository"] = "Features.Alerts.Sql",
        ["ApiClientRepository"] = "Features.Auth.Sql",
        ["LogEntryRepository"] = "Features.Logs.Sql",
        ["MessageTemplateRepository"] = "Features.Templates.Sql",
        ["OptOutRepository"] = "Features.OptOuts.Sql",
        ["PortalUserManagementRepository"] = "Features.Administration.Sql",
        ["PortalUserRepository"] = "Features.Auth.Sql",
        ["RefreshTokenRepository"] = "Features.Auth.Sql",
        ["SmsMessageRepository"] = "Features.Messages.Sql",
        ["SmsReportRepository"] = "Features.Reports.Sql",
        ["TenantAiSettingsRepository"] = "Features.Messages.Sql",
        ["TenantConfigurationCache"] = "Shared.Tenancy.Sql",
        ["TenantPortalRepository"] = "Features.Overview.Sql",
        ["TenantPortalUserManagementRepository"] = "Features.Administration.Sql",
        ["TenantProvisioner"] = "Features.Tenants.Sql",
        ["TenantRateLimitRepository"] = "Features.Administration.Sql",
        ["TenantRepository"] = "Features.Tenants.Sql",
        ["TenantSmsOverviewOutbox"] = "Features.Overview.Sql",
        ["TenantSmsOverviewProjection"] = "Features.Overview.Sql",
        ["TenantSmsProviderRepository"] = "Features.Providers.Sql",
        ["TenantTimeZoneProvider"] = "Shared.Tenancy.Sql"
    };

    public static string Load(string path) => Cache.GetOrAdd(path, static value =>
    {
        var resourceName = ResourcePrefix + ResolveResourcePath(value);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static string ResolveResourcePath(string path)
    {
        var slash = path.IndexOf('/');
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;
        var owner = fileName.Split('.')[0];

        if (path.StartsWith("Persistence/", StringComparison.Ordinal) && PersistenceOwners.TryGetValue(owner, out var persistenceOwner))
            return $"{persistenceOwner}.{fileName}";

        if (path.StartsWith("Messaging/", StringComparison.Ordinal))
        {
            var area = owner.StartsWith("Alert", StringComparison.Ordinal) || owner == "EvaluateAlertRuleEvent"
                ? "Features.Alerts.Sql"
                : owner.StartsWith("TenantSmsOverview", StringComparison.Ordinal)
                    ? "Features.Overview.Sql"
                    : owner.StartsWith("Sms", StringComparison.Ordinal)
                        ? "Features.Messages.Sql"
                        : "Shared.Messaging.Sql";
            return $"{area}.{fileName}";
        }

        if (path.StartsWith("Observability/", StringComparison.Ordinal))
            return $"Shared.Observability.Sql.{fileName}";

        if (path.StartsWith("Provision/", StringComparison.Ordinal))
            return $"{(fileName.StartsWith("CreateTenant", StringComparison.Ordinal) ? "Features.Tenants.Sql" : "Features.Auth.Sql")}.{fileName}";

        return path.Replace('/', '.');
    }
}
