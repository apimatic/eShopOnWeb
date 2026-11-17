using System;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

public static class PaymentRequestMapping
{
    public static AddressData? ToAddressData(this AddressRequest? address) =>
        address is null
            ? null
            : new AddressData(address.Street, address.City, address.State, address.Country, address.ZipCode);

    public static CardData? ToCardData(this CardRequest? card) =>
        card is null
            ? null
            : new CardData(card.Number,
                card.ExpirationMonth,
                card.ExpirationYear,
                card.Cvc,
                card.Name,
                card.Address.ToAddressData());

    public static long? ToAmountCents(decimal? amount, long? amountCents)
    {
        if (amountCents.HasValue)
        {
            return amountCents.Value;
        }

        return amount.HasValue
            ? (long)Math.Round(amount.Value * 100m, MidpointRounding.AwayFromZero)
            : null;
    }
}
