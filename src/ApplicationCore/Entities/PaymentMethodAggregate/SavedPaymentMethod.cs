using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse on later orders. Full card details are NEVER
/// stored here — only PayPal's vault token and a safe descriptor the shopper can recognise.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string lastFourDigits,
        string? expiryMonth, string? expiryYear, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Label = label;
    }

    /// <summary>Identity of the shopper who owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id used to pay with this card later. Not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network (e.g. VISA, MASTERCARD) — safe to display.</summary>
    public string? CardBrand { get; private set; }

    /// <summary>Last four digits — safe to display so the shopper recognises the card.</summary>
    public string LastFourDigits { get; private set; }

    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }

    /// <summary>Optional shopper-supplied label/nickname.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
