using System.Text.Json;
using Sms.Application.Administration;

namespace Sms.Infrastructure.Providers;

public sealed class TwilioSettingsPolicy : IProviderSettingsPolicy
{
    public ProviderDefinition Definition => new("Twilio", "Account SID", "Auth Token", []);

    public string? MergeAndValidate(string? existing, IReadOnlyDictionary<string, string?> changes)
    {
        if (changes.Count != 0)
            throw new ArgumentException("This provider does not support additional settings.");

        return existing;
    }

    public (IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> ConfiguredSecrets) Describe(string? settings) =>
        (new Dictionary<string, string?>(), []);
}

public sealed class BandwidthSettingsPolicy : IProviderSettingsPolicy
{
    private const int MaxSettingLength = 200;
    private const int MaxSecretLength = 512;

    public ProviderDefinition Definition => new(
        "Bandwidth",
        "OAuth Client ID",
        "OAuth Client Secret",
        [
            new("accountId", "Messaging Account ID", false, true),
            new("applicationId", "Application ID", false, true),
            new("webhookPassword", "Webhook password", true, true)
        ]);

    public string MergeAndValidate(string? existing, IReadOnlyDictionary<string, string?> changes)
    {
        var values = Read(existing);

        foreach (var change in changes)
        {
            var field = Definition.Fields.FirstOrDefault(field => field.Key == change.Key)
                ?? throw new ArgumentException("This provider setting is not supported.");

            if (field.Secret && string.IsNullOrWhiteSpace(change.Value))
                continue;

            values[field.Key] = field.Secret
                ? change.Value
                : change.Value?.Trim();
        }

        foreach (var field in Definition.Fields)
        {
            values.TryGetValue(field.Key, out var value);
            var maxLength = field.Secret ? MaxSecretLength : MaxSettingLength;

            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
                throw new ArgumentException($"Enter a valid value for {field.Label}.");
        }

        return JsonSerializer.Serialize(values);
    }

    public (IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> ConfiguredSecrets) Describe(string? settings)
    {
        var values = Read(settings);

        var visibleSettings = Definition.Fields
            .Where(field => !field.Secret)
            .ToDictionary(field => field.Key, field => values.GetValueOrDefault(field.Key));

        var configuredSecrets = Definition.Fields
            .Where(field => field.Secret && !string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
            .Select(field => field.Key)
            .ToArray();

        return (visibleSettings, configuredSecrets);
    }

    private static Dictionary<string, string?> Read(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(settings) ?? new();
            return new(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            throw new ArgumentException("The existing provider settings are invalid.");
        }
    }
}

public sealed class MockSettingsPolicy : IProviderSettingsPolicy
{
    public ProviderDefinition Definition => new("Mock", "Test identifier", "Test secret", []);

    public string? MergeAndValidate(string? existing, IReadOnlyDictionary<string, string?> changes)
    {
        if (changes.Count != 0)
            throw new ArgumentException("Mock provider does not support additional settings.");

        return existing;
    }

    public (IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> ConfiguredSecrets) Describe(string? settings) =>
        (new Dictionary<string, string?>(), []);
}
