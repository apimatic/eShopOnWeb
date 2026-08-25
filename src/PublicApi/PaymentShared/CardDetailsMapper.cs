using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

public static class CardDetailsMapper
{
    public static PayPalCardDetails ToPayPalCardDetails(this CardDetailsRequestDto card)
    {
        var expiry = $"{card.ExpiryYear}-{card.ExpiryMonth.PadLeft(2, '0')}";
        var billingAddress = new PayPalAddress(
            card.BillingAddress.CountryCode,
            card.BillingAddress.AddressLine1,
            card.BillingAddress.City,
            card.BillingAddress.State,
            card.BillingAddress.PostalCode,
            card.BillingAddress.AddressLine2);

        return new PayPalCardDetails(card.CardholderName, card.Number, expiry, card.Cvv, billingAddress);
    }
}
