using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. Belongs to exactly one shopper (<see cref="BuyerId"/>). Holds
/// only PayPal's vault id plus a safe descriptor (brand, last digits, expiry) — never the full card
/// number, which lives solely in PayPal's vault.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id. Used as the funding source for later orders.</summary>
    public string VaultId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }

    /// <summary>Card expiry as PayPal returns it (e.g. "2027-01").</summary>
    public string Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultId, string brand, string lastDigits, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand ?? string.Empty;
        LastDigits = lastDigits ?? string.Empty;
        Expiry = expiry ?? string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
