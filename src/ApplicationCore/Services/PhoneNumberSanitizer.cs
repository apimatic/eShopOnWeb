using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Strips phone-number-like digit sequences so they are never written to logs or stored error text.
/// </summary>
public static class PhoneNumberSanitizer
{
    private static readonly Regex PhoneLike = new(
        @"(\+?\d[\d\s().\-]{6,}\d)",
        RegexOptions.Compiled);

    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
