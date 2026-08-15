using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself is held in PayPal's vault; this app
/// stores only the vault token and a safe, non-sensitive description (brand + last four + expiry)
/// so the shopper can recognise which card it is. Full card details are never stored here.
/// A saved card belongs to the shopper who saved it (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string vaultId,
        string cardBrand,
        string last4,
        string expiryMonth,
        string expiryYear,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CardholderName = cardholderName;
    }

    /// <summary>Owner of the saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>Last four digits — safe to show, never the full number.</summary>
    public string Last4 { get; private set; }

    public string ExpiryMonth { get; private set; }
    public string ExpiryYear { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
