using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. This app
/// stores only PayPal's vault token id and a safe description (brand, last four digits,
/// expiry) — never the full card number. A saved card belongs to the shopper who saved it.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>The owning shopper (the buyer id / username from the auth token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault payment-token id — used to pay without re-entering the card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal's customer id this card is vaulted under, so a shopper's cards stay scoped to them.</summary>
    public string? PayPalCustomerId { get; private set; }

    // Safe, non-sensitive description so the shopper can recognise which card this is.
    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? cardBrand, string? lastFourDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
    }
}
