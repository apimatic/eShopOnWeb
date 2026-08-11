using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper saved for reuse. Only a safe description (brand, last four, expiry) plus the
/// PayPal vault token id (<see cref="CardId"/>) are kept here — never the full card number.
/// Part of the <see cref="Buyer"/> aggregate; created and removed only through it.
/// </summary>
public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string cardId, string brand, string last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        CardId = cardId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Optional shopper-chosen nickname for the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>
    /// The PayPal vault token id for this card. Actual card data lives in PayPal's PCI-compliant vault,
    /// never in this app's database. This token is what a later order pays with.
    /// </summary>
    public string CardId { get; private set; }

    /// <summary>Card brand, e.g. VISA — safe to show.</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits — safe to show.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry in YYYY-MM form — safe to show.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
