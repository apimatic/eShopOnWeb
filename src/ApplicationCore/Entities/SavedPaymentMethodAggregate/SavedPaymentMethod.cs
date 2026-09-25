using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

/// <summary>
/// A card a shopper saved (vaulted at PayPal) for reuse on later orders. Belongs to the shopper who
/// saved it — one shopper must never see, use, or delete another's. Holds only the PayPal vault
/// token id plus a safe descriptor (brand / last four / expiry); full card details are never stored.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? cardBrand, string? cardLast4,
        string? cardExpiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    /// <summary>PayPal-generated vault token id — used as <c>card.vault_id</c> to pay with this card.</summary>
    public string VaultId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? CardExpiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
