using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class Mappers
{
    public static PayPalRawCard ToRawCard(this CardRequest card)
    {
        PayPalBillingAddress? billing = null;
        if (card.BillingAddress is { } a)
        {
            billing = new PayPalBillingAddress(a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode, a.CountryCode);
        }
        return new PayPalRawCard(card.Number, card.Expiry, card.SecurityCode, card.Name, billing);
    }

    /// <summary>A safe, recognisable label for a saved card — brand plus masked last digits, never the full PAN.</summary>
    public static string ToDisplay(this SavedPaymentMethod card) => $"{card.Brand} ****{card.LastDigits}";

    public static SavedCardDto ToDto(this SavedPaymentMethod card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastDigits = card.LastDigits,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        Display = card.ToDisplay(),
        CreatedAt = card.CreatedAt
    };
}
