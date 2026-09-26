using System.Security.Cryptography;
using System.Text;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class TwilioWebhookValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidSignature()
    {
        const string url = "https://sms.example.com/api/v1/webhooks/twilio/inbound";
        const string token = "auth-token";
        var parameters = new[]
        {
            new KeyValuePair<string,string>("To", "+15550000002"),
            new KeyValuePair<string,string>("Body", "hello"),
            new KeyValuePair<string,string>("From", "+15550000001")
        };
        var signature = Sign(url, parameters, token);
        Assert.True(new TwilioWebhookValidator().Validate(url, parameters, signature, token));
    }

    [Fact]
    public void Validate_RejectsTamperedPayload()
    {
        const string url = "https://sms.example.com/hook";
        const string token = "auth-token";
        var original = new[] { new KeyValuePair<string,string>("Body", "hello") };
        var signature = Sign(url, original, token);
        var tampered = new[] { new KeyValuePair<string,string>("Body", "changed") };
        Assert.False(new TwilioWebhookValidator().Validate(url, tampered, signature, token));
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("signature", "")]
    public void Validate_RejectsMissingSecurityValues(string signature, string token) =>
        Assert.False(new TwilioWebhookValidator().Validate("https://example.com", [], signature, token));

    private static string Sign(string url, IEnumerable<KeyValuePair<string,string>> parameters, string token)
    {
        var value = new StringBuilder(url);
        foreach (var parameter in parameters.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal))
            value.Append(parameter.Key).Append(parameter.Value);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value.ToString())));
    }
}
