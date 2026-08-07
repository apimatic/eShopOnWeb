using System.Security.Claims;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class PaymentMappings
{
    /// <summary>The buyer id for the caller is their identity name (username/email) carried in the JWT -
    /// the same value the storefront uses as <c>Order.BuyerId</c>.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return buyerId;
    }

    public static PayPalCardDetails ToCardDetails(this CardRequest card)
    {
        PayPalBillingAddress? address = null;
        if (card.BillingAddress is { } b)
        {
            address = new PayPalBillingAddress(
                AddressLine1: b.AddressLine1,
                AdminArea2: b.City,
                AdminArea1: b.State,
                PostalCode: b.PostalCode,
                CountryCode: b.CountryCode);
        }

        return new PayPalCardDetails(
            Number: card.Number,
            Expiry: card.Expiry,
            SecurityCode: card.SecurityCode,
            CardholderName: card.CardholderName,
            BillingAddress: address);
    }
}
