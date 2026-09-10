using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    /// <summary>Shopper-friendly nickname.</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id. This is a reference token, never card
    /// data. Actual card details are held by PayPal's PCI-compliant vault, never here.</summary>
    public string CardId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; } // "YYYY-MM" as PayPal reports it
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string? alias, string? cardBrand, string? last4, string? expiry)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        CardId = cardId;
        Alias = alias;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
    }
}
