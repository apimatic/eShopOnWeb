using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an eShop shopper (BuyerId) to the PayPal customer id under which their vaulted cards are
/// grouped. PayPal's vault requires a customer id to list a shopper's tokens, so we allocate one
/// per shopper on first use and persist it.
/// </summary>
public class PaymentCustomer : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentCustomer() { }

    public PaymentCustomer(string buyerId, string payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal-side customer id (max 22 chars, pattern ^[0-9a-zA-Z_-]+$).</summary>
    public string PayPalCustomerId { get; private set; }
}
