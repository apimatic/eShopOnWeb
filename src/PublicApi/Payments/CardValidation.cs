using System;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class CardValidation
{
    public static CardDetails Normalize(
        string number,
        int expirationMonth,
        int expirationYear,
        string name,
        string? securityCode)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentApiException(400, "The card number is not valid.", "INVALID_CARD_NUMBER");
        }

        if (expirationMonth is < 1 or > 12)
        {
            throw new PaymentApiException(400, "The expiration month must be between 1 and 12.", "INVALID_EXPIRATION");
        }

        var year = expirationYear < 100 ? 2000 + expirationYear : expirationYear;

        if (year < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentApiException(400, "The card has expired.", "CARD_EXPIRED");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentApiException(400, "The cardholder name is required.", "MISSING_CARDHOLDER_NAME");
        }

        if (!string.IsNullOrWhiteSpace(securityCode)
            && (securityCode.Length is < 3 or > 4 || !securityCode.All(char.IsDigit)))
        {
            throw new PaymentApiException(400, "The security code is not valid.", "INVALID_SECURITY_CODE");
        }

        return new CardDetails
        {
            Number = digits,
            ExpirationMonth = expirationMonth,
            ExpirationYear = year,
            Name = name.Trim(),
            SecurityCode = securityCode
        };
    }
}
