using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Cheap shape checks before card data leaves the process. PayPal remains the authority.</summary>
public static class CardValidation
{
    public static void Validate(CardDetails card, DateTimeOffset now)
    {
        if (card is null) throw Invalid("Card details are required.");

        var number = card.Number ?? string.Empty;
        if (number.Length < 12 || number.Length > 19 || !number.All(char.IsAsciiDigit) || !PassesLuhn(number))
            throw Invalid("The card number is not valid.");

        if (!DateTime.TryParseExact(card.Expiry, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
            throw Invalid("The card expiry must be in YYYY-MM format.");
        if (new DateTimeOffset(expiry.Year, expiry.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1) <= now)
            throw Invalid("The card has expired.");

        if (card.SecurityCode is { } cvc && (cvc.Length is < 3 or > 4 || !cvc.All(char.IsAsciiDigit)))
            throw Invalid("The card security code must be 3 or 4 digits.");

        if (card.BillingAddress is { } address && (address.CountryCode?.Length != 2 || !address.CountryCode.All(char.IsAsciiLetter)))
            throw Invalid("The billing address country code must be a two-letter ISO country code.");
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    private static PaymentRequestException Invalid(string message) => new(PaymentErrorKind.Validation, "invalid_card", message);
}
