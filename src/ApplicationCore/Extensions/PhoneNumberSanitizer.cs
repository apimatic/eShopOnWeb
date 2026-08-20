using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

/// <summary>
/// Strips phone-number-like sequences so shopper destinations are never written to logs
/// or persisted error text.
/// </summary>
public static class PhoneNumberSanitizer
{
    private static readonly Regex PhoneLike = new(
        @"\+?\d[\d\s\-().]{6,}\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
