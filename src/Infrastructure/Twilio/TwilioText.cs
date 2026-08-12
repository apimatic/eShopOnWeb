using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>Helpers for keeping phone numbers out of text that might be logged or returned.</summary>
public static class TwilioText
{
    // Matches an optional '+' followed by 7 or more digits, allowing common separators.
    private static readonly Regex NumberLike =
        new(@"\+?\d[\d\-\s().]{5,}\d", RegexOptions.Compiled);

    /// <summary>Replaces anything that looks like a phone number with a placeholder.</summary>
    public static string? RedactNumbers(string? text) =>
        text is null ? null : NumberLike.Replace(text, "[redacted-number]");
}
