using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. Only a safe description is stored — never the PAN or CVV, which live
/// exclusively in PayPal's vault. Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalCustomerId,
        string vaultTokenId,
        string? cardBrand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultTokenId = vaultTokenId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal customer id these saved cards are grouped under.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>The PayPal vault payment-token id — used as <c>card.vault_id</c> to pay later.</summary>
    public string VaultTokenId { get; private set; }

    // --- safe display only ---
    public string? CardBrand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
