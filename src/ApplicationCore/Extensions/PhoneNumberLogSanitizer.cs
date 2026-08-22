using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class PhoneNumberLogSanitizer
{
    private static readonly Regex E164Like = new(@"\+?\d[\d\s().-]{7,20}\d", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return E164Like.Replace(value, "[redacted]");
    }
}
