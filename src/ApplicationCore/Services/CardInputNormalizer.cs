using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class CardInputNormalizer
{
    public static CardPaymentSource Normalize(CardPaymentSource card)
    {
        if (card == null)
        {
            throw new CheckoutException(400, "Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new CheckoutException(400, "Card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new CheckoutException(400, "Card expiry is required.");
        }

        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length < 13 || number.Length > 19)
        {
            throw new CheckoutException(400, "Card number is not a valid length.");
        }

        return new CardPaymentSource
        {
            Number = number,
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? "eShop Shopper" : card.Name.Trim(),
            BillingAddress = card.BillingAddress ?? new CardBillingAddress
            {
                AddressLine1 = "123 Main St.",
                AdminArea1 = "CA",
                AdminArea2 = "San Jose",
                PostalCode = "95131",
                CountryCode = "US"
            }
        };
    }

    public static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}$"))
        {
            return trimmed;
        }

        var slash = Regex.Match(trimmed, @"^(\d{1,2})\s*/\s*(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2
                ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)
                : int.Parse(yearPart, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12)
            {
                throw new CheckoutException(400, "Card expiry month is invalid.");
            }

            return $"{year:D4}-{month:D2}";
        }

        throw new CheckoutException(400, "Card expiry must be YYYY-MM or MM/YY.");
    }
}
