using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class LogSanitizer
{
    private static readonly Regex PhonePattern = new(@"\+?\d{8,15}", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhonePattern.Replace(value, "[redacted]");
    }
}
