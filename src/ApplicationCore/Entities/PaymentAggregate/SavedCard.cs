using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. The application stores only PayPal's vault
/// token and a safe descriptor (brand + last four + expiry) — never a full card number, which lives
/// only in PayPal's vault.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string? brand, string? last4, string? expiryMonth, string? expiryYear, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of this saved card (the shopper identity from the token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to pay with this card.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }

    /// <summary>Optional shopper-supplied label (e.g. "Personal Visa").</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
