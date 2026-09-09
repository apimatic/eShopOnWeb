using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself is never stored here: only PayPal's vault
/// token (used to charge it) plus safe, non-sensitive descriptors (brand, last four, expiry) so the
/// shopper can recognise which card it is. Belongs to the shopper identified by <see cref="BuyerId"/>.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string payPalCustomerId,
        string payPalVaultTokenId,
        string cardBrand,
        string cardLast4,
        string? cardholderName,
        string? cardExpiry,
        string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardholderName = cardholderName;
        CardExpiry = cardExpiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (the caller's identity / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal customer id this card is vaulted under (deterministic per shopper).</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>The durable PayPal vault token used to charge this card.</summary>
    public string PayPalVaultTokenId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>The last four digits, safe to show so the shopper can recognise the card.</summary>
    public string CardLast4 { get; private set; }

    public string? CardholderName { get; private set; }

    /// <summary>The card expiry in YYYY-MM, as reported by PayPal.</summary>
    public string? CardExpiry { get; private set; }

    /// <summary>An optional friendly label chosen by the shopper.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
