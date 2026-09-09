using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. The card itself lives in PayPal's vault;
/// this record keeps only the vault token id plus a safe, non-sensitive description
/// (brand, last four digits, expiry) so the shopper can recognise the card. Full card
/// details are never stored here.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string payPalVaultId, string? brand, string? last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        PayPalVaultId = payPalVaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A friendly, shopper-chosen name for the card (optional).</summary>
    public string? Alias { get; private set; }

    /// <summary>The PayPal vault token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The card network/brand, e.g. VISA (safe to show).</summary>
    public string? Brand { get; private set; }

    /// <summary>The last four digits of the card (safe to show).</summary>
    public string? Last4 { get; private set; }

    /// <summary>The card expiry in YYYY-MM form (safe to show).</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
