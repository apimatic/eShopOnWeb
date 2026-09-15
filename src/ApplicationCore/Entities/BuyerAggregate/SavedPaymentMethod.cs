using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this record
/// holds only the PayPal-generated vault token and safe descriptive metadata (brand, last four,
/// expiry) — never the PAN, CVV, or any full card details.
///
/// It is owned by the shopper who saved it (<see cref="BuyerId"/>); ownership is enforced by the
/// application when listing, using, or deleting it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string? cardBrand, string? last4, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this saved card (the authenticated user).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault token id used as the payment source for future orders.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal-generated customer id the vaulted card is associated with.</summary>
    public string PayPalCustomerId { get; private set; }

    // Safe, recognisable metadata only.
    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
