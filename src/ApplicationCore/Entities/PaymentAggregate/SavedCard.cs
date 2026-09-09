using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper vaulted at PayPal and can reuse for later orders. The application stores only
/// PayPal's vault id plus safe display fields (brand / last four / expiry) — never the PAN, CVV or
/// full card details. A saved card belongs to the shopper who saved it (scoped by <see cref="BuyerId"/>).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The owning shopper (matches the token's identity / Order.BuyerId).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault (payment token) id used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal-generated customer id the vaulted card is associated with.</summary>
    public string? PayPalCustomerId { get; private set; }

    // Safe display fields only — enough for the shopper to recognise the card.
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
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
