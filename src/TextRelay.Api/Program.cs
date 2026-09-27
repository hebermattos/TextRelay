using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Sms.Api.Filters;
using Sms.Api.Health;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sms.Api.Auth;
using Sms.Api.Middleware;
using Sms.Api.OpenApi;
using Sms.Api.RateLimiting;
using Sms.Application.Common;
using Sms.Api.Shared;
using Sms.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddSmsLogging();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(Sms.Infrastructure.Observability.TextRelayTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(Sms.Infrastructure.Observability.TextRelayTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddControllers();
builder.Services.AddScoped<PortalExceptionFilter>();
builder.Services.AddScoped<IAuditPipelineStep, ClientLoginAuditMiddleware>();
builder.Services.AddScoped<IAuditPipelineStep, PortalLoginAuditMiddleware>();
builder.Services.AddScoped<IAuditPipelineStep, PlatformAuditMiddleware>();
builder.Services.AddScoped<IAuditPipelineStep, RequestAuditMiddleware>();
builder.Services.AddScoped<IUserActivityWriter>(services => new PostgresUserActivityWriter(
    builder.Configuration.GetConnectionString("LogsPostgres")!,
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILogger<PostgresUserActivityWriter>>()));
builder.Services.AddScoped<IPlatformActivityWriter>(services => new PostgresPlatformActivityWriter(
    builder.Configuration.GetConnectionString("LogsPostgres")!,
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<ILogger<PostgresPlatformActivityWriter>>()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<IWorkerTenantContext>(services => services.GetRequiredService<HttpTenantContext>());
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents { OnTokenValidated = PortalSecurity.ValidateTenantAsync };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization(PortalSecurity.ConfigureAuthorization);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddRedisConnection(builder.Configuration);
builder.Services.AddTextRelay(builder.Configuration);
builder.Services.AddSingleton<IRateLimitCounter, RedisRateLimitCounter>();
builder.Services.AddDependencyHealthChecks(builder.Configuration);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseSwaggerDocumentation();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseMiddleware<AuditPipelineMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantRateLimitMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapSwaggerRoot();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.Run();

public partial class Program;
