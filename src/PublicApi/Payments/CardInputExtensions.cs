using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class CardInputExtensions
{
    public static CardDetails ToCardDetails(this CardInput input) => new(
        input.Number,
        input.Expiry,
        input.SecurityCode,
        input.CardholderName,
        input.BillingAddress is null
            ? null
            : new CardBillingAddress(
                input.BillingAddress.AddressLine1,
                input.BillingAddress.City,
                input.BillingAddress.State,
                input.BillingAddress.PostalCode,
                input.BillingAddress.CountryCode));
}
