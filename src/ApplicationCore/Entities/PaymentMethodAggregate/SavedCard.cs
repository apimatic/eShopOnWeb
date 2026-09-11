using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The real card data lives only in PayPal's vault; this
/// row holds the PayPal vault token plus a safe description (brand + last four + expiry) so the
/// shopper can recognise the card. Full card details are never stored here or logged.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string vaultTokenId, string payPalCustomerId, string brand, string last4, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this card (their username / token name claim).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge this card later.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>The PayPal-generated customer id this card is vaulted under (shared per shopper).</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>Card network, e.g. VISA. Safe to display.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits. Safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry in YYYY-MM. Safe to display.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
