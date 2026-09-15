using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper vaulted at PayPal and can reuse to pay later orders.
/// The application only ever stores the PayPal vault token plus a safe descriptor
/// (brand / last four / expiry) — never the card number or security code.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultToken, string? brand, string? last4,
        string? expiry, string? alias, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultToken, nameof(vaultToken));

        BuyerId = buyerId;
        VaultToken = vaultToken;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = createdAt;
    }

    /// <summary>Owner of the card — the shopper's identity name (JWT subject).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id used as the payment source for later orders.</summary>
    public string VaultToken { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
