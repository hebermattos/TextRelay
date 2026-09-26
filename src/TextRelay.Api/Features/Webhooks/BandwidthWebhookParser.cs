using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Providers;

public sealed class BandwidthWebhookParser(ITenantSmsProviderRepository providers) : ISmsWebhookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<SmsWebhookBatch> ParseAsync(string authorization, Stream body, SmsDirection direction,
        CancellationToken cancellationToken = default)
    {
        var credentials = ReadCredentials(authorization);
        if (credentials is null) return new(SmsWebhookResult.Unauthorized, []);

        Callback?[]? callbacks;
        try
        {
            callbacks = await JsonSerializer.DeserializeAsync<Callback?[]>(body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return new(SmsWebhookResult.Invalid, []);
        }

        if (callbacks is null || callbacks.Length is < 1 or > 100)
            return new(SmsWebhookResult.Invalid, []);

        var events = new List<ValidatedSmsWebhook>(callbacks.Length);
        foreach (var callback in callbacks)
        {
            var message = callback?.Message;
            var status = MapStatus(callback?.Type, direction);
            if (callback is null || message is null || status is null || callback.Time is null
                || !Valid(message.Id, 200) || !Valid(message.ApplicationId, 200)
                || !Valid(message.Owner, 32) || !Valid(message.From, 32) || !Valid(callback.To, 32)
                || message.To is not { Length: > 0 } || message.To.Any(number => !Valid(number, 32))
                || !message.To.Contains(callback.To, StringComparer.Ordinal)
                || (direction == SmsDirection.Inbound
                    ? message.Direction != "in" || message.Owner != callback.To
                    : message.Direction != "out" || message.Owner != message.From))
                return new(SmsWebhookResult.Invalid, []);

            // The username is the configured OAuth client ID, not a tenant identifier.
            // Account + owner selects a candidate only; the secret and application must also match.
            var configuration = await providers.GetByAccountAndNumberAsync(
                "Bandwidth", credentials.Value.Username, message.Owner!, cancellationToken);
            if (configuration is null || !configuration.IsActive
                || configuration.Provider != "Bandwidth" || configuration.AccountId != credentials.Value.Username
                || configuration.FromNumber != message.Owner
                || !Authenticate(configuration.Settings, credentials.Value.Password, message.ApplicationId!))
                return new(SmsWebhookResult.Unauthorized, []);

            events.Add(new(configuration.TenantId, "Bandwidth", message.Id!, direction, status.Value,
                message.From!, direction == SmsDirection.Inbound ? message.Owner! : callback.To!,
                direction == SmsDirection.Inbound ? message.Text ?? string.Empty : string.Empty, callback.Time.Value));
        }

        return new(SmsWebhookResult.Accepted, events);
    }

    private static bool Authenticate(string? json, string password, string applicationId)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        WebhookSettings? settings;
        try { settings = JsonSerializer.Deserialize<WebhookSettings>(json, JsonOptions); }
        catch (JsonException) { return false; }

        if (settings is null || string.IsNullOrWhiteSpace(settings.WebhookPassword)) return false;
        var secretMatches = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(password)),
            SHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookPassword)));
        return secretMatches && string.Equals(settings.ApplicationId, applicationId, StringComparison.Ordinal);
    }

    private static (string Username, string Password)? ReadCredentials(string authorization)
    {
        if (authorization.Length > 4096 || !AuthenticationHeaderValue.TryParse(authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter)) return null;
        try
        {
            var decoded = StrictUtf8.GetString(Convert.FromBase64String(header.Parameter));
            var separator = decoded.IndexOf(':');
            if (separator is < 1 or > 200 || separator == decoded.Length - 1) return null;
            return (decoded[..separator], decoded[(separator + 1)..]);
        }
        catch (FormatException) { return null; }
        catch (DecoderFallbackException) { return null; }
    }

    private static bool Valid(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static SmsStatus? MapStatus(string? type, SmsDirection direction) => (direction, type) switch
    {
        (SmsDirection.Inbound, "message-received") => SmsStatus.Received,
        (SmsDirection.Outbound, "message-sending" or "message-sent") => SmsStatus.Sent,
        (SmsDirection.Outbound, "message-delivered") => SmsStatus.Delivered,
        (SmsDirection.Outbound, "message-failed") => SmsStatus.Failed,
        _ => null
    };

    private sealed record WebhookSettings(string? ApplicationId, string? WebhookPassword);
    private sealed record Callback(string? Type, DateTimeOffset? Time, string? To, CallbackMessage? Message);
    private sealed record CallbackMessage(string? Id, string? Owner, string? ApplicationId,
        string? Direction, string? From, string?[]? To, string? Text);
}
