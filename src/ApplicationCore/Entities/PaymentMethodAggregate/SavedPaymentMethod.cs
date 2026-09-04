using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card the shopper saved to the provider's vault. Stores only the token ids the
/// provider handed back and display-safe card metadata (brand, last four, expiry) —
/// never card numbers, and never anything that can be logged into a payment.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string externalId,
        string buyerId,
        string vaultCustomerId,
        string vaultTokenId,
        string brand,
        string last4,
        string expiry,
        string cardholderName)
    {
        Guard.Against.NullOrEmpty(externalId, nameof(externalId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        ExternalId = externalId;
        BuyerId = buyerId;
        VaultCustomerId = vaultCustomerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Public identifier returned as "paymentMethodId"; the int Id stays internal.</summary>
    public string ExternalId { get; private set; }

    public string BuyerId { get; private set; }

    /// <summary>Provider-side vault customer id (PayPal created it implicitly).</summary>
    public string VaultCustomerId { get; private set; }

    /// <summary>Provider-side payment-token id referenced when paying with this card.</summary>
    public string VaultTokenId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>MM/YYYY as reported by the provider token.</summary>
    public string Expiry { get; private set; }

    public string CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
