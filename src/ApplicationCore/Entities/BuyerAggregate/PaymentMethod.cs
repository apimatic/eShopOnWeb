using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application never stores the card
/// itself — only PayPal's vault token id plus a safe descriptor (brand, last four
/// digits, expiry) the shopper can use to recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultId, string? brand, string? last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved the card; every access is scoped to this.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault (payment token) id — the only handle to the card. Never the PAN.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network, e.g. VISA. Safe to show.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits, so the shopper can recognise the card. Safe to show.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry (e.g. "2030-01"). Safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional friendly label chosen by the shopper.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
