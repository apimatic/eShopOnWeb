using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class PhoneNumberSanitizer
{
    private static readonly Regex PhonePattern = new(@"\+?\d[\d\s\-().]{8,}\d", RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return PhonePattern.Replace(text, "[redacted]");
    }
}
