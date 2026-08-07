using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>Validates and maps an incoming <see cref="CardDto"/> to the domain <see cref="CardDetails"/>.</summary>
public static class CardMapping
{
    public static CardDetails ToCardDetails(CardDto? dto)
    {
        if (dto is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Number))
        {
            throw new PaymentValidationException("Card number is required.");
        }
        if (string.IsNullOrWhiteSpace(dto.SecurityCode))
        {
            throw new PaymentValidationException("Card security code is required.");
        }
        if (dto.BillingAddress is null || string.IsNullOrWhiteSpace(dto.BillingAddress.CountryCode))
        {
            throw new PaymentValidationException("A billing address with a country code is required.");
        }

        var number = dto.Number.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
        var expiry = NormalizeExpiry(dto.Expiry);

        var billing = new BillingAddress(
            countryCode: dto.BillingAddress.CountryCode.Trim().ToUpperInvariant(),
            addressLine1: NullIfBlank(dto.BillingAddress.AddressLine1),
            addressLine2: NullIfBlank(dto.BillingAddress.AddressLine2),
            adminArea1: NullIfBlank(dto.BillingAddress.AdminArea1),
            adminArea2: NullIfBlank(dto.BillingAddress.AdminArea2),
            postalCode: NullIfBlank(dto.BillingAddress.PostalCode));

        return new CardDetails(number, expiry, dto.SecurityCode.Trim(), NullIfBlank(dto.Name), billing);
    }

    /// <summary>Normalizes an expiry to PayPal's <c>YYYY-MM</c> form. Accepts YYYY-MM, MM/YY, MM/YYYY, MM-YY.</summary>
    public static string NormalizeExpiry(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new PaymentValidationException("Card expiry is required.");
        }

        var value = input.Trim();

        var iso = Regex.Match(value, @"^(\d{4})-(0[1-9]|1[0-2])$");
        if (iso.Success)
        {
            return value;
        }

        var slash = Regex.Match(value, @"^(\d{1,2})[/-](\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value);
            if (month is < 1 or > 12)
            {
                throw new PaymentValidationException("Card expiry month must be between 01 and 12.");
            }

            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart) : int.Parse(yearPart);
            return $"{year:D4}-{month:D2}";
        }

        throw new PaymentValidationException("Card expiry must be in YYYY-MM or MM/YY format.");
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
