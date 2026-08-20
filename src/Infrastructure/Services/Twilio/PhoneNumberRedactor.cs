using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class PhoneNumberRedactor
{
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s().-]{6,}\d", RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted]");
    }
}
