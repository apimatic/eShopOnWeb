using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app only keeps
/// the vault token and enough safe display detail (brand, last four, expiry) for the shopper to
/// recognise the card. No PAN / CVV is ever stored here.
/// A saved card belongs to the shopper who saved it — every query is scoped by <see cref="BuyerId"/>.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string vaultId, string? customerId, string brand, string last4,
        string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        CustomerId = customerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this card (their token identity / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the vault token is attached to.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }

    public string Last4 { get; private set; }

    /// <summary>Expiry as PayPal reports it, "YYYY-MM".</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
