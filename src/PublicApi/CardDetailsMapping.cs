using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi;

public static class CardDetailsMapping
{
    public static CardDetails ToCardDetails(this CardDetailsRequest request) => new(
        request.Name,
        request.Number,
        request.Expiry,
        request.SecurityCode,
        request.CountryCode,
        request.AddressLine1,
        request.AddressLine2,
        request.City,
        request.State,
        request.PostalCode);
}
