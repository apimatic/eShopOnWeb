using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// Maps an eShop shopper (<see cref="BuyerId"/>) to the PayPal-generated customer id that groups
/// their vaulted payment tokens. PayPal generates the customer id on the first vault; we persist
/// it so later vaults reuse the same customer and <c>ListCustomerPaymentTokens</c> can enumerate
/// the shopper's saved cards.
/// </summary>
public class PayPalCustomerRef : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PayPalCustomerRef() { }

    public PayPalCustomerRef(string buyerId, string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
    }

    /// <summary>The shopper (JWT identity name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's generated customer id for this shopper.</summary>
    public string PayPalCustomerId { get; private set; }
}
