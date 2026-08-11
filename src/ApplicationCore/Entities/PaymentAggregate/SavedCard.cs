using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives only in PayPal's vault; this app stores
/// the vault token id plus a safe, non-sensitive description (brand, last four, expiry, holder name).
/// Full card details (PAN, CVV) are never stored here. A saved card belongs to exactly one owner.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>The owning shopper's identity (JWT name). One shopper never sees another's cards.</summary>
    public string OwnerId { get; private set; }

    /// <summary>PayPal-generated vault token id, referenced as payment_source.card.vault_id when paying.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id that groups this shopper's vaulted cards.</summary>
    public string PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }

    public string Last4 { get; private set; }

    /// <summary>Card expiry in ISO YYYY-MM form, as returned by PayPal.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    /// <summary>Optional shopper-chosen nickname to help recognise the card.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string ownerId, string vaultId, string payPalCustomerId, string brand, string last4,
        string expiry, string? cardholderName, string? label)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        OwnerId = ownerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        Label = label;
    }

    /// <summary>A human-friendly, safe description of the card, e.g. "VISA ending 1111 (exp 2030-01)".</summary>
    public string DisplayName() => $"{Brand} ending {Last4} (exp {Expiry})";
}
