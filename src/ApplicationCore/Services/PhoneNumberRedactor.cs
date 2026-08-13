using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Scrubs anything that looks like a phone number out of free text before it is logged or stored as a
/// provider error, so a shopper's number is never written to a log even when it is echoed back inside
/// a provider's error message.
/// </summary>
public static class PhoneNumberRedactor
{
    // A run of 7+ digits, optionally starting with '+' and allowing spaces/dashes/parens between them.
    private static readonly Regex PhoneLike =
        new(@"\+?\d[\d\-\s().]{5,}\d", RegexOptions.Compiled);

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return PhoneLike.Replace(text, "[redacted]");
    }
}
