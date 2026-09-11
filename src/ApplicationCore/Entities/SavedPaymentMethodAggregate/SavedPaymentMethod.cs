using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) so a later order can be paid without
/// re-entering it. The application never stores the full card details; it keeps only the
/// PayPal vault token and a safe descriptor (brand and last digits) so the shopper can
/// recognise which card it is.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalCustomerId,
        string payPalVaultId,
        string? cardBrand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal customer id this card is vaulted under (stable per shopper).</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>The PayPal-generated vault token id used to pay with the card.</summary>
    public string PayPalVaultId { get; private set; }

    public string? CardBrand { get; private set; }

    /// <summary>The last two-to-four digits of the card — never the full number.</summary>
    public string? LastDigits { get; private set; }

    /// <summary>The card expiry in YYYY-MM form.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
