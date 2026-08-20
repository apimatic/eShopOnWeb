using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class PiiRedactor
{
    private static readonly Regex PhonePattern = new(
        @"\+?\d[\d\s\-().]{6,}\d",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhonePattern.Replace(value, "[redacted]");
    }
}
