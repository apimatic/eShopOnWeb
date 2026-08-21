using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class PhoneNumberSanitizer
{
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s\-().]{8,}\d", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
