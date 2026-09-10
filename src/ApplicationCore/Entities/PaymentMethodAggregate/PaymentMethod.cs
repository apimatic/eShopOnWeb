using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders.
/// The application never stores full card details — only PayPal's vault token id and a
/// safe display (brand, last four digits, expiry) so the shopper can recognise the card.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(
        string buyerId,
        string payPalVaultId,
        string payPalCustomerId,
        string? cardBrand,
        string? lastFourDigits,
        string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved this card. Used for ownership scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token id — used as <c>payment_source.card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id these cards are grouped under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
}
