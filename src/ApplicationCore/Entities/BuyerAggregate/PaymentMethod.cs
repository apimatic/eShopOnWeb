using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card saved by a shopper for reuse. Only a safe description is kept (brand, last four, expiry)
/// plus the PayPal vault token that references the card in PayPal's PCI-compliant vault — the full
/// card number is never stored in this app's database.
/// </summary>
public class PaymentMethod : BaseEntity
{
    public string? Alias { get; private set; }

    /// <summary>PayPal vault token id — the reference to the card stored in PayPal's PCI vault.</summary>
    public string? CardId { get; private set; }

    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; } // "YYYY-MM"
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string? brand, string? last4, string? expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        CardId = cardId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
