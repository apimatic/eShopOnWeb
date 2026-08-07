using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. Full card details are never stored here — only a
/// non-sensitive descriptor plus the token (<see cref="CardId"/>) returned by the PCI-compliant
/// vault (PayPal), which is what we present back to the vault to charge the card later.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string alias, string cardId, string last4, string? brand, string? expiryMonthYear)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        Alias = alias;
        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        ExpiryMonthYear = expiryMonthYear;
    }

    /// <summary>A friendly label the shopper can recognise, e.g. "Visa ending 1111".</summary>
    public string? Alias { get; private set; }

    /// <summary>The vault token id. Actual card data lives in the PCI-compliant vault, not here.</summary>
    public string? CardId { get; private set; }

    public string? Last4 { get; private set; }

    /// <summary>Card network as reported by the vault (e.g. VISA), when available.</summary>
    public string? Brand { get; private set; }

    /// <summary>Expiry as YYYY-MM, when reported by the vault.</summary>
    public string? ExpiryMonthYear { get; private set; }
}
