using System.Security.Cryptography;
using System.Text;

namespace Sms.Infrastructure.Providers;

public sealed class TwilioWebhookValidator
{
    public bool Validate(string url, IEnumerable<KeyValuePair<string, string>> parameters, string signature, string authToken)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(authToken)) return false;

        var value = new StringBuilder(url);
        foreach (var parameter in parameters.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal))
        {
            value.Append(parameter.Key);
            value.Append(parameter.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value.ToString())));
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(signature);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
