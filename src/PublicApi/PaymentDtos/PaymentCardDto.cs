using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentDtos;

/// <summary>
/// Raw card details for a one-off payment or a save. Never serialized back out of the API,
/// never persisted, never logged.
/// </summary>
public class PaymentCardDto
{
    public string? CardholderName { get; set; }

    public string CardNumber { get; set; } = string.Empty;

    /// <summary>MM/YYYY (MM/YY is accepted and normalized).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public CardBillingAddressDto? BillingAddress { get; set; }

    /// <summary>Shape-checks and normalizes the card, returning the gateway credential.</summary>
    public CardCredential ToCredential()
    {
        var digits = Regex.Replace(CardNumber ?? string.Empty, @"[\s-]", "");
        if (!Regex.IsMatch(digits, @"^\d{13,19}$"))
        {
            throw new ValidationFailureException("Card number must be 13 to 19 digits.");
        }

        var expiry = (Expiry ?? string.Empty).Trim().Replace('-', '/');
        var match = Regex.Match(expiry, @"^(0[1-9]|1[0-2])/(20)?(\d{2})$");
        if (!match.Success)
        {
            throw new ValidationFailureException("Card expiry must be MM/YYYY or MM/YY.");
        }
        var month = int.Parse(match.Groups[1].Value);
        var year = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value + match.Groups[3].Value)
            : 2000 + int.Parse(match.Groups[3].Value);
        if (new DateTime(year, month, 1).AddMonths(1) <= DateTime.UtcNow)
        {
            throw new ValidationFailureException("Card expiry must be a future date.");
        }
        expiry = $"{month:D2}/{year:D4}";

        var cvc = (SecurityCode ?? string.Empty).Trim();
        if (!Regex.IsMatch(cvc, @"^\d{3,4}$"))
        {
            throw new ValidationFailureException("Security code must be 3 or 4 digits.");
        }

        var name = string.IsNullOrWhiteSpace(CardholderName) ? null : CardholderName.Trim();

        return new CardCredential(
            Number: digits,
            Expiry: expiry,
            SecurityCode: cvc,
            CardholderName: name,
            BillingAddress: BillingAddress?.ToCredential());
    }
}

public class CardBillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    public CardBillingAddress ToCredential()
    {
        var country = (CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (country.Length != 2 || !country.All(char.IsLetter))
        {
            throw new ValidationFailureException("Billing address country code must be a two-letter ISO-3166 code.");
        }
        return new CardBillingAddress(country, Street?.Trim(), City?.Trim(), PostalCode?.Trim());
    }
}
