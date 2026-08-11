using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Billing address for a card. Only country code is required.</summary>
public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

/// <summary>
/// Raw card details supplied by the caller. Never stored or logged by this app — passed straight to
/// PayPal. Expiry may be "YYYY-MM", "MM/YY" or "MM/YYYY"; it is normalised to PayPal's "YYYY-MM".
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public static class CardModelExtensions
{
    public static RawCard ToRawCard(this CardDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Number))
        {
            throw new InvalidPaymentRequestException("A card number is required.");
        }
        if (string.IsNullOrWhiteSpace(dto.SecurityCode))
        {
            throw new InvalidPaymentRequestException("A card security code is required.");
        }

        var billing = dto.BillingAddress is null
            ? null
            : new CardBillingAddress(
                CountryCode: string.IsNullOrWhiteSpace(dto.BillingAddress.CountryCode) ? "US" : dto.BillingAddress.CountryCode,
                AddressLine1: dto.BillingAddress.AddressLine1,
                AddressLine2: dto.BillingAddress.AddressLine2,
                AdminArea2: dto.BillingAddress.City,
                AdminArea1: dto.BillingAddress.State,
                PostalCode: dto.BillingAddress.PostalCode);

        return new RawCard(
            Number: dto.Number.Replace(" ", string.Empty),
            Expiry: NormalizeExpiry(dto.Expiry),
            SecurityCode: dto.SecurityCode,
            Name: dto.Name,
            BillingAddress: billing);
    }

    /// <summary>Normalise an expiry to PayPal's "YYYY-MM" format.</summary>
    private static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new InvalidPaymentRequestException("A card expiry is required (YYYY-MM).");
        }

        expiry = expiry.Trim();

        // Already YYYY-MM.
        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}$"))
        {
            return expiry;
        }

        // MM/YY or MM/YYYY.
        var slash = Regex.Match(expiry, @"^(\d{1,2})\/(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2
                ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)
                : int.Parse(yearPart, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12)
            {
                throw new InvalidPaymentRequestException("Card expiry month must be between 01 and 12.");
            }
            return $"{year:D4}-{month:D2}";
        }

        throw new InvalidPaymentRequestException("Card expiry must be in YYYY-MM, MM/YY or MM/YYYY format.");
    }
}
