using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class PiiRedactor
{
    private static readonly Regex PhoneLike = new(
        @"\+\d{6,15}",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
