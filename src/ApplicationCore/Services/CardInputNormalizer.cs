using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class CardInputNormalizer
{
    private static readonly Regex IsoExpiry = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex SlashExpiry = new(@"^(0[1-9]|1[0-2])\/(\d{2}|\d{4})$", RegexOptions.Compiled);

    public static CardPaymentInput Normalize(CardPaymentInput card)
    {
        if (card == null)
        {
            throw new PaymentException(400, "Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException(400, "Card number, expiry, and security code are required.");
        }

        var number = new string(Array.FindAll(card.Number.ToCharArray(), char.IsDigit));
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException(400, "Card number must be 13 to 19 digits.");
        }

        return card with
        {
            Number = number,
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? null : card.Name.Trim()
        };
    }

    public static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (IsoExpiry.IsMatch(trimmed))
        {
            return trimmed;
        }

        var match = SlashExpiry.Match(trimmed);
        if (!match.Success)
        {
            throw new PaymentException(400, "Card expiry must be YYYY-MM or MM/YY.");
        }

        var month = match.Groups[1].Value;
        var yearPart = match.Groups[2].Value;
        var year = yearPart.Length == 2
            ? (2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)
            : yearPart;
        return $"{year}-{month}";
    }
}
