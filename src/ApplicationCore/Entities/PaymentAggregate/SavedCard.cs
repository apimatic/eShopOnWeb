using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse (Flow 2). The card itself lives in PayPal's vault; this app only
/// keeps the PayPal vault token id plus a safe, non-sensitive description (brand, last four, expiry)
/// so the shopper can recognise which card it is. No full card number is ever stored here.
///
/// The card belongs to the shopper who saved it (<see cref="BuyerId"/>); ownership is enforced at the
/// service layer so one shopper can never see, use, or delete another's card.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The identity of the shopper who owns this card (the token's name claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault token id used to charge the card on future orders.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>The last four digits, for display only.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Card expiry in ISO-8601 YYYY-MM, for display only.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalVaultId, string? brand, string? last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
