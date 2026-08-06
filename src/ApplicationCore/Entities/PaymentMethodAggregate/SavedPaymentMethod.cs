using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted with PayPal) for reuse on later orders.
///
/// Only PayPal's vault token and a safe, non-reversible descriptor of the card (brand, last four
/// digits, expiry) are ever persisted here — never the full PAN, CVC, or anything that could
/// reconstruct the card. The card itself lives in PayPal's vault; <see cref="VaultId"/> references it.
///
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>); repositories always scope
/// queries by buyer so one shopper can never see, use, or delete another's card.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string lastFourDigits,
        string expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        VaultId = Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        CardBrand = Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        LastFourDigits = Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper — the authenticated username the card was saved under.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment token) id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network, e.g. "VISA" — safe to show.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the card — safe to show so the shopper recognises the card.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in "YYYY-MM" form — safe to show.</summary>
    public string Expiry { get; private set; }

    /// <summary>Optional cardholder name as entered when saving.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
