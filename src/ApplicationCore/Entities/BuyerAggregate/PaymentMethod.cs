using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse ("vaulted"). It belongs to exactly one shopper
/// (<see cref="BuyerId"/>) and is only ever visible, usable or deletable by that shopper.
/// <para>
/// Full card details are NEVER stored here or anywhere in the application's own database — only
/// PayPal's vault token (<see cref="CardId"/>) and safe display metadata (brand / last four / expiry).
/// </para>
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultId, string? cardBrand, string last4,
        int? expiryMonth, int? expiryYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        CardId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Alias = cardholderName;
    }

    /// <summary>The owning shopper's identity (username / email). A saved card is scoped to its owner.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Cardholder name / friendly alias. Not sensitive; safe to display.</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault token id. Full card data lives only in PayPal's PCI-compliant vault.</summary>
    public string CardId { get; private set; } // actual card data must be stored in a PCI compliant system, like PayPal's vault

    /// <summary>Card network / brand, e.g. "VISA". Safe to display.</summary>
    public string? CardBrand { get; private set; }

    /// <summary>Last four digits of the card. Safe to display.</summary>
    public string Last4 { get; private set; }

    public int? ExpiryMonth { get; private set; }
    public int? ExpiryYear { get; private set; }

    /// <summary>A safe description a shopper recognises, e.g. "VISA ending in 1111 (exp 12/2030)". Never full card details.</summary>
    public string Description
    {
        get
        {
            var brand = string.IsNullOrWhiteSpace(CardBrand) ? "Card" : CardBrand;
            var expiry = ExpiryMonth is > 0 && ExpiryYear is > 0
                ? $" (exp {ExpiryMonth:00}/{ExpiryYear})"
                : string.Empty;
            return $"{brand} ending in {Last4}{expiry}";
        }
    }
}
