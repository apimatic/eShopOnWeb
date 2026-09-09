using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper saved for reuse. The actual card data lives only in PayPal's vault
/// (PCI-compliant); this app stores the opaque PayPal vault id plus a safe descriptor
/// (brand + last 4 + expiry) so the shopper can recognise the card. Full card details
/// (PAN, CVV) are never stored here or anywhere in this application's database.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>A friendly label the shopper can give the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>Opaque PayPal vault payment-token id. Used to pay later; not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card brand (e.g. VISA, MASTERCARD), as reported by PayPal. May be unknown.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits only — safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form, as reported by PayPal. Safe to display.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string? brand, string? last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
