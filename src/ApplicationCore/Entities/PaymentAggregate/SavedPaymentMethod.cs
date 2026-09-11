using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's Vault; this app only keeps
/// the vault token id and a safe description (brand + last four + expiry). Full card details are never stored.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id used as the <c>vault_id</c> when paying.</summary>
    public string PaymentTokenId { get; private set; }

    /// <summary>PayPal-generated customer id, shared across all of this buyer's saved cards.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string paymentTokenId, string? customerId, string brand, string last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paymentTokenId, nameof(paymentTokenId));

        BuyerId = buyerId;
        PaymentTokenId = paymentTokenId;
        CustomerId = customerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
