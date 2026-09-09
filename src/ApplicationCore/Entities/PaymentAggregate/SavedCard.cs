using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application stores only PayPal's vault token id and a
/// safe display of the card (brand, last four, expiry) — never the full card number, which lives
/// only in PayPal's vault.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string cardBrand, string lastFour, string expiry,
        string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFour = lastFour;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner; a card belongs only to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>Last four digits, safe to show so the shopper can recognise the card.</summary>
    public string LastFour { get; private set; }

    /// <summary>Expiry in YYYY-MM form (safe to show).</summary>
    public string Expiry { get; private set; }

    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
