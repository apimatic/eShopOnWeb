using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Lightweight, non-PCI validation of card input shape before it is sent to PayPal. Never logs the card.</summary>
public static class CardValidation
{
    private static readonly Regex ExpiryPattern = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    public static void Validate(CardDetails? card)
    {
        if (card is null)
            throw new PaymentValidationException("Card details are required.");

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
            throw new PaymentValidationException("Card number must be between 12 and 19 digits.");

        if (string.IsNullOrWhiteSpace(card.Expiry) || !ExpiryPattern.IsMatch(card.Expiry))
            throw new PaymentValidationException("Card expiry must be in YYYY-MM format.");

        var cvc = new string((card.SecurityCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cvc.Length < 3 || cvc.Length > 4)
            throw new PaymentValidationException("Card security code must be 3 or 4 digits.");

        if (string.IsNullOrWhiteSpace(card.CardholderName))
            throw new PaymentValidationException("Cardholder name is required.");
    }
}
