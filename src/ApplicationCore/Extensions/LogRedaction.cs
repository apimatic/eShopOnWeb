using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class LogRedaction
{
    private static readonly Regex PhoneNumberPattern = new(
        @"\+?\d[\d\s\-\(\).]{6,}\d",
        RegexOptions.Compiled);

    public static string RedactPhoneNumbers(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneNumberPattern.Replace(value, "[redacted]");
    }
}
