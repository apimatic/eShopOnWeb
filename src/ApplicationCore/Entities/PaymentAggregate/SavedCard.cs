using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The app never stores the card number — only PayPal's vault
/// token id plus a safe descriptor (brand, last four, expiry) so the shopper can recognise it.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(
        string buyerId,
        string vaultTokenId,
        string? payPalCustomerId,
        string? brand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved the card (JWT identity/email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault token id used to pay later (never the card number).</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>PayPal customer id the token is grouped under, when PayPal assigned one.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
