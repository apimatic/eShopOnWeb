using System;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class CardDetailsMapper
{
    private static readonly Regex ExpiryFormat = new(@"^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    public static CardDetails ToCardDetails(CardDetailsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Number) || string.IsNullOrWhiteSpace(dto.SecurityCode))
        {
            throw new ArgumentException("Card number and securityCode are required.");
        }
        if (!ExpiryFormat.IsMatch(dto.Expiry ?? string.Empty))
        {
            throw new ArgumentException("Card expiry must be in YYYY-MM format.");
        }

        return new CardDetails(
            dto.Number.Replace(" ", string.Empty),
            dto.Expiry,
            dto.SecurityCode,
            dto.Name,
            dto.BillingAddress is null
                ? null
                : new CardBillingAddress(
                    dto.BillingAddress.AddressLine1,
                    dto.BillingAddress.AddressLine2,
                    dto.BillingAddress.AdminArea2,
                    dto.BillingAddress.AdminArea1,
                    dto.BillingAddress.PostalCode,
                    dto.BillingAddress.CountryCode));
    }
}
