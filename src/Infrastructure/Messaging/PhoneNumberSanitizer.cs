using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class PhoneNumberSanitizer
{
    private static readonly Regex PhoneLike = new(@"\+?\d{10,15}", RegexOptions.Compiled);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(text, "[redacted]");
    }
}
