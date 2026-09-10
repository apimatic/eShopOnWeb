using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. This application stores only the PayPal vault
/// token id plus a safe descriptor (brand, last four, expiry) — never the full card number or CVC.
/// A saved card belongs to the shopper who saved it (<see cref="BuyerId"/>).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string? brand, string? lastDigits,
        string? expiry, string? cardholderName, string? payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        PayPalCustomerId = payPalCustomerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id — the handle used to charge the card later.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    /// <summary>PayPal customer id the token is grouped under (if any).</summary>
    public string? PayPalCustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
