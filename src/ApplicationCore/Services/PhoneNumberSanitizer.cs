using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class PhoneNumberSanitizer
{
    private static readonly Regex NumberPattern = new(@"\+?\d[\d\s().-]{8,}\d", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return NumberPattern.Replace(value, "[redacted]");
    }
}
