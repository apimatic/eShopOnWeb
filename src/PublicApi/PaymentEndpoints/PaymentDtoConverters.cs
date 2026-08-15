using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentDtoConverters
{
    public static CardPaymentDetails ToDomain(this CardDto card) => new(
        Number: card.Number,
        ExpiryMonth: card.ExpiryMonth,
        ExpiryYear: card.ExpiryYear,
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress?.ToBillingAddress());

    public static CardBillingAddress ToBillingAddress(this AddressDto a) => new(
        AddressLine1: a.Street,
        AddressLine2: null,
        AdminArea2: a.City,
        AdminArea1: a.State,
        PostalCode: a.ZipCode,
        CountryCode: a.Country);

    public static Address? ToShippingAddress(this AddressDto? a) =>
        a is null ? null : new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);
}
