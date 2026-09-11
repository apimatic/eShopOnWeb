using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse. Only a safe description of the card
/// is kept — brand, last four digits and expiry month — never the full number or security code.
/// The <see cref="VaultTokenId"/> is PayPal's handle for the vaulted card and is what a later
/// payment references. A saved card belongs to the shopper who saved it (<see cref="BuyerId"/>).
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string cardBrand, string cardLast4, string cardExpiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string CardBrand { get; private set; }
    public string CardLast4 { get; private set; }
    public string CardExpiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A shopper-friendly label, e.g. "VISA ****1111".</summary>
    public string Description => $"{CardBrand} ****{CardLast4}";
}
