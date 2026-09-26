using System.Text.RegularExpressions;

namespace Sms.Application.Templates;

public static partial class MessageTemplateRenderer
{
    public static readonly IReadOnlySet<string> SystemVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "recipientName", "recipientPhone", "tenantName"
    };

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex VariableRegex();

    public static IReadOnlyList<string> Variables(string body) =>
        VariableRegex().Matches(body).Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Render(string body, IReadOnlyDictionary<string, string> customValues,
        IReadOnlyDictionary<string, string>? systemValues = null)
    {
        var values = new Dictionary<string, string>(customValues, StringComparer.OrdinalIgnoreCase);
        if (systemValues is not null)
            foreach (var value in systemValues) values[value.Key] = value.Value;

        var missing = Variables(body).Where(name => !values.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Missing template variables: {string.Join(", ", missing)}.");

        return VariableRegex().Replace(body, match => values[match.Groups[1].Value]);
    }
}
