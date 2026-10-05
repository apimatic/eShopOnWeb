using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Rejects malformed card input before it reaches the processor. Messages never echo card data.</summary>
public static class CardValidator
{
    public static void Validate(CardDetails card, DateTimeOffset now)
    {
        if (card.Number.Length is < 13 or > 19 || !card.Number.All(char.IsAsciiDigit) || !PassesLuhn(card.Number))
            throw new PaymentValidationException("Card number is not valid.");

        if (!DateTime.TryParseExact(card.Expiry, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
            throw new PaymentValidationException("Card expiry must be in YYYY-MM format.");
        if (new DateTime(expiry.Year, expiry.Month, 1).AddMonths(1) <= now.UtcDateTime)
            throw new PaymentValidationException("Card has expired.");

        if (card.SecurityCode is not null && (card.SecurityCode.Length is < 3 or > 4 || !card.SecurityCode.All(char.IsAsciiDigit)))
            throw new PaymentValidationException("Card security code must be 3 or 4 digits.");

        if (card.NameOnCard is { Length: > 300 })
            throw new PaymentValidationException("Name on card is too long.");

        if (card.BillingAddress is { } address &&
            (address.CountryCode.Length != 2 || !address.CountryCode.All(char.IsAsciiLetterUpper)))
            throw new PaymentValidationException("Billing address country code must be a two-letter ISO code (e.g. US).");
    }

    /// <summary>Normalises user input: strips spaces and dashes from the number.</summary>
    public static string NormaliseNumber(string? number) =>
        new((number ?? string.Empty).Where(c => c != ' ' && c != '-').ToArray());

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
}
