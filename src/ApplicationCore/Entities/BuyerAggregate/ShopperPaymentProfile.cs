using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// Maps a signed-in shopper (by their identity/buyer id) to the PayPal Vault customer id under which their
/// saved cards live. PayPal generates the customer id on the first saved card; we persist it so later saves,
/// listing and deletion all operate under the same customer — which is also what scopes saved cards to their
/// owner: a shopper only ever sees or acts on tokens under their own customer id.
/// </summary>
public class ShopperPaymentProfile : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private ShopperPaymentProfile() { }

    public ShopperPaymentProfile(string buyerId, string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
}
