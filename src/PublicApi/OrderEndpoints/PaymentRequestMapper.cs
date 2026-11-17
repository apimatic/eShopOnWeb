using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class PaymentRequestMapper
{
    public static CardDetails ToCardDetails(CardRequest request) =>
        new(
            request.Number,
            request.ExpirationMonth,
            request.ExpirationYear,
            request.SecurityCode,
            request.Name,
            ToAddress(request.BillingAddress));

    private static CardAddress? ToAddress(CardAddressRequest? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Line1))
        {
            return null;
        }

        return new CardAddress(
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.Country);
    }
}
