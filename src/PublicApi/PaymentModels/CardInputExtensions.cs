using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public static class CardInputExtensions
{
    /// <summary>Maps the API card input onto the provider-neutral domain <see cref="CardDetails"/>.</summary>
    public static CardDetails ToCardDetails(this CardInput card)
    {
        BillingAddress? billing = null;
        if (card.BillingAddress is { } b)
        {
            billing = new BillingAddress(b.AddressLine1, b.AddressLine2, b.City, b.State, b.PostalCode, b.CountryCode);
        }

        return new CardDetails(
            card.CardholderName,
            card.Number,
            card.ExpiryMonth,
            card.ExpiryYear,
            card.SecurityCode,
            billing);
    }
}
