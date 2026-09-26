using System.Net.Http.Json;
using System.Text.Json;
using Sms.Application.Messages;

namespace Sms.Infrastructure.Providers;

public sealed class OllamaMessageAssistant(HttpClient client, ITenantAiSettingsRepository settings) : IMessageAssistant
{
    private const string Model = "qwen2.5:0.5b";

    public async Task<MessageAssistantResult> ImproveAsync(Guid tenantId, string message, CancellationToken cancellationToken = default) =>
        await AskAsync((await settings.GetAsync(tenantId, cancellationToken)).ImprovePrompt, message, false, cancellationToken);

    public async Task<MessageAssistantResult> ValidateAsync(Guid tenantId, string message, CancellationToken cancellationToken = default) =>
        await AskAsync((await settings.GetAsync(tenantId, cancellationToken)).ValidatePrompt, message, true, cancellationToken);

    private async Task<MessageAssistantResult> AskAsync(string instruction, string message, bool validation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message is required.");
        if (message.Length > 4000) throw new ArgumentException("Message cannot exceed 4000 characters.");

        using var response = await client.PostAsJsonAsync("api/generate", new
        {
            model = Model,
            prompt = $"{instruction}\n\nSMS:\n{message}",
            stream = false,
            options = new { temperature = 0.2 }
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken) ??
                     throw new InvalidOperationException("Ollama returned an empty response.");

        if (!validation)
            return new MessageAssistantResult(result.Response.Trim(), Array.Empty<string>(), true);

        try
        {
            var json = ExtractJson(result.Response);
            var validationResult = JsonSerializer.Deserialize<ValidationResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new MessageAssistantResult(message, validationResult?.Issues ?? Array.Empty<string>(), validationResult?.IsValid ?? false);
        }
        catch (JsonException)
        {
            return new MessageAssistantResult(message, new[] { "The local AI could not return a valid validation result." }, false);
        }
    }

    private static string ExtractJson(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end < start) throw new JsonException();
        return value[start..(end + 1)];
    }

    private sealed record OllamaResponse(string Response);
    private sealed record ValidationResponse(bool IsValid, string[] Issues);
}
