using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault (PCI-compliant);
/// this app keeps only the PayPal vault token id and a safe descriptor (brand + last 4 + expiry)
/// so the shopper can recognise which card it is. Full card details are never stored here.
///
/// Owned by exactly one shopper (<see cref="BuyerId"/>); one shopper never sees another's cards.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the owning shopper (the token's name/email claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id used to pay with this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card brand as PayPal reported it (e.g. VISA).</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits, for recognition only.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry as reported by PayPal, "YYYY-MM".</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-friendly label.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultId, string brand, string last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
