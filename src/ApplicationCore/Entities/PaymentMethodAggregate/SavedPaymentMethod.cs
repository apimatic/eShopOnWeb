using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted with PayPal) to reuse on later orders. The application never
/// stores the card number, CVV or expiry-secured data — only PayPal's vault token id plus safe
/// descriptors (brand and last four digits) so the shopper can recognise which card it is.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string? cardBrand, string? cardLast4, string? cardExpiry, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this card (JWT identity name). Enforced on every read/use/delete.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault token id used as the payment source when charging this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id this card is vaulted under, reused across the shopper's cards.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>Expiry as YYYY-MM (safe to show; not sufficient to reconstruct the card).</summary>
    public string? CardExpiry { get; private set; }

    /// <summary>An optional shopper-friendly label.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
