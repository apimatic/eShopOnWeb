using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. The card itself lives in PayPal's vault; this app only
/// keeps the vault token (<see cref="CardId"/>) plus a safe description (brand, last 4, expiry) so
/// the shopper can recognise which card it is. Full card details are never stored here.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string? alias, string last4, string? cardBrand, int? expiryMonth, int? expiryYear)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        CardId = cardId;
        Alias = alias;
        Last4 = last4;
        CardBrand = cardBrand;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Shopper-supplied nickname for the card (optional).</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault token id — actual card data is stored in PayPal's PCI-compliant vault, not here.</summary>
    public string? CardId { get; private set; }

    public string? Last4 { get; private set; }

    /// <summary>Card network/brand (e.g. VISA), as reported by PayPal.</summary>
    public string? CardBrand { get; private set; }

    public int? ExpiryMonth { get; private set; }

    public int? ExpiryYear { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
