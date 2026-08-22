using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class CardRequestMapper
{
    public static CardDetails? ToCardDetails(CardRequestDto? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.Number))
        {
            return null;
        }

        return new CardDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            AddressLine1 = card.BillingAddress?.AddressLine1,
            AddressLine2 = card.BillingAddress?.AddressLine2,
            AdminArea2 = card.BillingAddress?.AdminArea2,
            AdminArea1 = card.BillingAddress?.AdminArea1,
            PostalCode = card.BillingAddress?.PostalCode,
            CountryCode = card.BillingAddress?.CountryCode ?? "US"
        };
    }
}
