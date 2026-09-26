using Sms.Application.Templates;

namespace Sms.Infrastructure.Tests;

public sealed class MessageTemplateRendererTests
{
    [Fact]
    public void TemplateVariablesAreDistinctAndCaseInsensitive()
    {
        var variables = MessageTemplateRenderer.Variables("Hi {{ recipientName }}, {{Code}} {{code}}.");
        Assert.Equal(2, variables.Count);
        Assert.Contains("recipientName", variables);
        Assert.Contains("Code", variables);
    }

    [Fact]
    public void TemplateRenderCombinesCustomAndSystemValuesCaseInsensitively()
    {
        var result = MessageTemplateRenderer.Render(
            "{{recipientName}}: {{code}}",
            new Dictionary<string, string> { ["CODE"] = "123" },
            new Dictionary<string, string> { ["RecipientName"] = "Ana" });

        Assert.Equal("Ana: 123", result);
    }

    [Fact]
    public void TemplateRenderReportsAllMissingVariables()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            MessageTemplateRenderer.Render("{{first}} {{second}}", new Dictionary<string, string>()));

        Assert.Contains("first", error.Message);
        Assert.Contains("second", error.Message);
    }

    [Fact]
    public void SystemVariablesContainSupportedRecipientAndTenantValues()
    {
        Assert.Contains("recipientName", MessageTemplateRenderer.SystemVariables);
        Assert.Contains("recipientPhone", MessageTemplateRenderer.SystemVariables);
        Assert.Contains("tenantName", MessageTemplateRenderer.SystemVariables);
    }
}
