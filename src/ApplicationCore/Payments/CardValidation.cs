using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Lightweight, PayPal-spec-aligned validation of raw card input so obviously malformed requests are
/// rejected with a 400 before any card data leaves the process. Mirrors the patterns in the PayPal
/// OpenAPI schemas (number 13-19 digits, expiry YYYY-MM, security code 3-4 digits).
/// </summary>
public static class CardValidation
{
    private static readonly Regex NumberPattern = new("^[0-9]{13,19}$", RegexOptions.Compiled);
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private static readonly Regex SecurityCodePattern = new("^[0-9]{3,4}$", RegexOptions.Compiled);

    public static void Validate(CardDetails card)
    {
        if (card is null)
        {
            throw new PaymentInputException("Card details are required.");
        }

        var number = card.Number?.Replace(" ", string.Empty).Replace("-", string.Empty) ?? string.Empty;
        if (!NumberPattern.IsMatch(number))
        {
            throw new PaymentInputException("Card number must be 13 to 19 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || !ExpiryPattern.IsMatch(card.Expiry))
        {
            throw new PaymentInputException("Card expiry must be in YYYY-MM format.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) || !SecurityCodePattern.IsMatch(card.SecurityCode))
        {
            throw new PaymentInputException("Card security code must be 3 or 4 digits.");
        }
    }

    /// <summary>Returns the PAN with spaces and dashes stripped, ready to send to PayPal.</summary>
    public static string NormalizeNumber(string number) =>
        (number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
}
