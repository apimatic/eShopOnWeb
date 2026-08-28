using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card the shopper saved for reuse. The card itself lives in PayPal's vault — this row holds only
/// the vault reference plus enough non-sensitive detail for the shopper to recognise the card.
/// No card number, expiry-with-CVV or security code is ever stored here.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper who saved the card. Every read and delete is scoped to this value.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's payment-token id — what gets sent as <c>card.vault_id</c> to pay.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal's customer id, reused so a shopper's cards stay under one vault customer.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }

    /// <summary>Expiry as PayPal reports it, <c>YYYY-MM</c>. Harmless on its own.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
