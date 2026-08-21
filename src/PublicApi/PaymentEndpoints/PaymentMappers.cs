using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentMappers
{
    public static CardInput ToCardInput(CardRequest card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.CardholderName,
        card.BillingLine1,
        card.BillingLine2,
        card.BillingState,
        card.BillingCity,
        card.BillingPostalCode,
        card.CountryCode ?? string.Empty);
}
