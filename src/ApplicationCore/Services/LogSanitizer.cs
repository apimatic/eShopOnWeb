using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Defensive helper to keep shopper phone numbers out of logs. Any provider- or exception-supplied
/// string that might echo a number is passed through here before it is logged.
/// </summary>
public static class LogSanitizer
{
    // Matches E.164-ish sequences (optionally +, then 7+ digits with common separators).
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\-\s().]{6,}\d", RegexOptions.Compiled);

    public static string RedactPhoneNumbers(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhoneLike.Replace(value, "[redacted-number]");
    }
}
