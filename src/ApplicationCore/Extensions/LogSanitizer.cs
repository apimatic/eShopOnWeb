using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Extensions;

public static class LogSanitizer
{
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s\-().]{6,}\d", RegexOptions.Compiled);

    public static string RedactPhoneNumbers(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
