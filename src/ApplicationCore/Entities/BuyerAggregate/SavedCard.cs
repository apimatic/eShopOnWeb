using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives in PayPal's PCI-compliant vault;
/// this app stores only the vault token and safe display metadata (brand + last four + expiry).
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultId, string? customerId,
        string brand, string last4, string? expiry, string? label)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CustomerId = customerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper's identity (username from the JWT).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card again.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal vault customer id the token is grouped under, if any.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>Card expiry as reported by PayPal, e.g. "2028-05".</summary>
    public string? Expiry { get; private set; }

    /// <summary>Optional shopper-chosen nickname for the card.</summary>
    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
