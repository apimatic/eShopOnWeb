using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Masks phone-number-like sequences out of provider-supplied error text before it is stored or
/// logged, so a shopper's number is never written to logs even when the provider echoes it back.
/// </summary>
public static class PhoneNumberScrubber
{
    private static readonly Regex NumberLike = new(@"\+?\d[\d\-\s().]{5,}\d", RegexOptions.Compiled);

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return NumberLike.Replace(text, "[redacted-number]");
    }
}
