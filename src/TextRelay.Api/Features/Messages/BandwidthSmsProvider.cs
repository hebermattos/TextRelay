using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Sms.Application.Common;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Providers;

public sealed class BandwidthSmsProvider(
    HttpClient messagingClient,
    IHttpClientFactory httpClientFactory,
    ITenantContext tenantContext,
    ITenantSmsProviderRepository configurations,
    IDistributedCache cache) : ISmsProvider
{
    public string Name => "Bandwidth";

    public async Task<ProviderSendResult> SendAsync(string from, string to, string body, CancellationToken cancellationToken = default)
    {
        var config = await configurations.GetAsync(tenantContext.TenantId, Name, cancellationToken)
            ?? throw new InvalidOperationException("Bandwidth is not configured for this tenant.");
        var settings = ParseSettings(config.Settings);
        var sender = string.IsNullOrWhiteSpace(from) ? config.FromNumber : from;
        if (string.IsNullOrWhiteSpace(sender)) throw new InvalidOperationException("Bandwidth From number is not configured.");
        if (!string.IsNullOrWhiteSpace(config.FromNumber) && !string.Equals(sender, config.FromNumber, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested From number is not configured for this tenant.");

        var token = await GetAccessTokenAsync(config.AccountId, config.ApiSecret, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v2/users/{Uri.EscapeDataString(settings.AccountId)}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { to = new[] { to }, from = sender, text = body, applicationId = settings.ApplicationId });
        HttpResponseMessage response;
        try { response = await messagingClient.SendAsync(request, cancellationToken); }
        catch (HttpRequestException exception) { throw new TransientSmsProviderException("Bandwidth is temporarily unavailable.", exception); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode == 429 || statusCode >= 500)
                    throw new TransientSmsProviderException($"Bandwidth temporarily returned HTTP {statusCode}.");
                throw new HttpRequestException($"Bandwidth returned HTTP {statusCode}.");
            }

            var result = await response.Content.ReadFromJsonAsync<BandwidthMessageResponse>(cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(result?.Id)) throw new InvalidOperationException("Bandwidth response did not include a message id.");
            return new ProviderSendResult(result.Id, SmsStatus.Pending);
        }

    }

    private async Task<string> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var cacheKey = BuildAccessTokenCacheKey(tenantContext.TenantId, clientId, clientSecret);
        var cachedToken = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cachedToken)) return cachedToken;

        var client = httpClientFactory.CreateClient("BandwidthOAuth");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "client_credentials" });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Bandwidth OAuth returned HTTP {(int)response.StatusCode}.");

        var token = await response.Content.ReadFromJsonAsync<OAuthResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new InvalidOperationException("Bandwidth OAuth response did not include an access token.");

        if (token.ExpiresIn > 0)
        {
            var lifetimeSeconds = Math.Max(1, token.ExpiresIn - 10);
            await cache.SetStringAsync(cacheKey, token.AccessToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(lifetimeSeconds)
                },
                cancellationToken);
        }

        return token.AccessToken;
    }

    private static string BuildAccessTokenCacheKey(Guid tenantId, string clientId, string clientSecret)
    {
        var material = Encoding.UTF8.GetBytes($"{clientId}\0{clientSecret}");
        return $"bandwidth:oauth:{tenantId:N}:{Convert.ToHexString(SHA256.HashData(material))}";
    }

    private static BandwidthSettings ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Bandwidth provider settings are required.");
        var settings = JsonSerializer.Deserialize<BandwidthSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (string.IsNullOrWhiteSpace(settings?.AccountId) || string.IsNullOrWhiteSpace(settings.ApplicationId))
            throw new InvalidOperationException("Bandwidth settings must contain accountId and applicationId.");
        return settings;
    }

    private sealed record BandwidthSettings(string AccountId, string ApplicationId);
    private sealed record OAuthResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
    private sealed record BandwidthMessageResponse(string Id);
}
