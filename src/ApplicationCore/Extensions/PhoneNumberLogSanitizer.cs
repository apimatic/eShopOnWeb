using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class PhoneNumberLogSanitizer
{
    private static readonly Regex E164Pattern = new(@"\+\d{7,15}", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return E164Pattern.Replace(value, "+***");
    }
}
