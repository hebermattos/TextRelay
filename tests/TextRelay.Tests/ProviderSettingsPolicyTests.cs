using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class ProviderSettingsPolicyTests
{
    [Fact]
    public void TwilioPolicy_DescribesNoExtraSettingsAndRejectsChanges()
    {
        var policy = new TwilioSettingsPolicy();
        Assert.Equal("Twilio", policy.Definition.Name);
        Assert.Equal("existing", policy.MergeAndValidate("existing", new Dictionary<string, string?>()));
        var described = policy.Describe(null);
        Assert.Empty(described.Values);
        Assert.Empty(described.ConfiguredSecrets);
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null,
            new Dictionary<string, string?> { ["unsupported"] = "value" }));
    }

    [Fact]
    public void MockPolicy_DescribesNoExtraSettingsAndRejectsChanges()
    {
        var policy = new MockSettingsPolicy();
        Assert.Equal("Mock", policy.Definition.Name);
        Assert.Equal("existing", policy.MergeAndValidate("existing", new Dictionary<string, string?>()));
        var described = policy.Describe(null);
        Assert.Empty(described.Values);
        Assert.Empty(described.ConfiguredSecrets);
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null,
            new Dictionary<string, string?> { ["unsupported"] = "value" }));
    }

    [Fact]
    public void BandwidthPolicy_CreatesTrimsAndDescribesSettingsWithoutExposingSecret()
    {
        var policy = new BandwidthSettingsPolicy();
        var json = policy.MergeAndValidate(null, new Dictionary<string, string?>
        {
            ["accountId"] = " account ",
            ["applicationId"] = " app ",
            ["webhookPassword"] = " secret "
        });

        var described = policy.Describe(json);
        Assert.Equal("account", described.Values["accountId"]);
        Assert.Equal("app", described.Values["applicationId"]);
        Assert.False(described.Values.ContainsKey("webhookPassword"));
        Assert.Equal("webhookPassword", Assert.Single(described.ConfiguredSecrets));
    }

    [Fact]
    public void BandwidthPolicy_PreservesSecretWhenBlankChangeIsSubmitted()
    {
        var policy = new BandwidthSettingsPolicy();
        var original = policy.MergeAndValidate(null, new Dictionary<string, string?>
        {
            ["accountId"] = "account",
            ["applicationId"] = "app",
            ["webhookPassword"] = "secret"
        });
        var updated = policy.MergeAndValidate(original, new Dictionary<string, string?>
        {
            ["accountId"] = "updated",
            ["webhookPassword"] = " "
        });

        var described = policy.Describe(updated);
        Assert.Equal("updated", described.Values["accountId"]);
        Assert.Equal("webhookPassword", Assert.Single(described.ConfiguredSecrets));
    }

    [Fact]
    public void BandwidthPolicy_RejectsUnsupportedMissingLongAndCorruptSettings()
    {
        var policy = new BandwidthSettingsPolicy();
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null,
            new Dictionary<string, string?> { ["unknown"] = "x" }));
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null,
            new Dictionary<string, string?> { ["accountId"] = "account" }));
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null, new Dictionary<string, string?>
        {
            ["accountId"] = new string('a', 201),
            ["applicationId"] = "app",
            ["webhookPassword"] = "secret"
        }));
        Assert.Throws<ArgumentException>(() => policy.MergeAndValidate(null, new Dictionary<string, string?>
        {
            ["accountId"] = "account",
            ["applicationId"] = "app",
            ["webhookPassword"] = new string('s', 513)
        }));
        Assert.Throws<ArgumentException>(() => policy.Describe("{invalid-json"));
    }

    [Fact]
    public void BandwidthPolicy_ReadsSettingsCaseInsensitively()
    {
        var policy = new BandwidthSettingsPolicy();
        var json = """{"ACCOUNTID":"account","APPLICATIONID":"app","WEBHOOKPASSWORD":"secret"}""";
        var described = policy.Describe(json);
        Assert.Equal("account", described.Values["accountId"]);
        Assert.Equal("app", described.Values["applicationId"]);
        Assert.Single(described.ConfiguredSecrets);
    }
}
