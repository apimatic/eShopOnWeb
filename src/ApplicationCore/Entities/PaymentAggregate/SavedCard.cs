using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application stores only the PayPal vault token id and a
/// safe descriptor (brand, last four digits, expiry) — never the full card number, CVV, or any
/// data that would make this PCI-relevant. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? brand, string? last4, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card (the JWT identity name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id — the reference used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the token is grouped under, if returned.</summary>
    public string? PayPalCustomerId { get; private set; }

    // ---- safe descriptor only ----
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
