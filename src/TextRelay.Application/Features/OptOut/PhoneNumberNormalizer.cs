namespace Sms.Application.OptOut;

public static class PhoneNumberNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Any(character => !char.IsDigit(character) && character is not '+' and not ' ' and not '-' and not '(' and not ')'))
            throw new ArgumentException("Phone number contains unsupported characters.");
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 15) throw new ArgumentException("Phone number must contain between 8 and 15 digits.");
        return $"+{digits}";
    }
}
