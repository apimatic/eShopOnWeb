using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. Holds only the PayPal vault token id plus a safe descriptor
/// (brand + last four + expiry) — never full card details, which live only in PayPal's vault.
/// Belongs to the shopper who saved it; another shopper can never see, use, or delete it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? cardBrand, string? lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper (token identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id — used as card.vault_id when paying with this saved card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal-generated customer id this token is attached to (for cross-checking the vault).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }

    /// <summary>Last four digits only — safe to show so the shopper recognises the card.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM (safe to show).</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
