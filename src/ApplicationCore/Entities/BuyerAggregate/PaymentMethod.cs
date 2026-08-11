using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. The full card details are never stored here —
/// they live only in PayPal's vault. This entity keeps the vault token plus a safe
/// description (brand + last four + expiry) so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string vaultId, string? alias, string brand, string last4, string? expiry)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        VaultId = vaultId;
        Alias = alias;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    /// <summary>Optional friendly name the shopper gave the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id. This is what a later order pays with.</summary>
    public string? VaultId { get; private set; }

    /// <summary>Card network (e.g. VISA). Safe to display.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits. Safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry in YYYY-MM form. Safe to display.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
