using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class PhoneNumberRedactor
{
    private static readonly Regex PhoneLike = new(@"\+\d{7,15}", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
