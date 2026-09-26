using System.Text.Json;
using Sms.Infrastructure.Observability;

namespace Sms.Infrastructure.Tests;

public sealed class LogAttributeSanitizerTests
{
    [Fact]
    public void Serialize_KeepsOnlyExplicitlyAllowedAttributes()
    {
        var tenantId = Guid.NewGuid();
        var attributes = new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["Provider"] = "Twilio",
            ["StatusCode"] = 202,
            ["Authorization"] = "Bearer secret-token",
            ["Password"] = "secret-password",
            ["Body"] = "private message",
            ["PhoneNumber"] = "+15550000000",
            ["{OriginalFormat}"] = "Sending {Body} with {Authorization}"
        };

        var json = LogAttributeSanitizer.Serialize(attributes);

        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(tenantId.ToString(), root.GetProperty("TenantId").GetString());
        Assert.Equal("Twilio", root.GetProperty("Provider").GetString());
        Assert.Equal(202, root.GetProperty("StatusCode").GetInt32());
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+15550000000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_ReturnsNullWhenNoSafeAttributesExist()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["ClientSecret"] = "secret",
            ["SmsBody"] = "private"
        };

        Assert.Null(LogAttributeSanitizer.Serialize(attributes));
        Assert.Null(LogAttributeSanitizer.Serialize(null));
    }
}
