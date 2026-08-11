using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse on later orders. Only a safe description of
/// the card is kept in the application's own database — the card number, expiry and CVC are
/// never stored here; they live only in PayPal's vault, referenced by <see cref="PayPalVaultTokenId"/>.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultTokenId, string payPalCustomerId,
        string? cardBrand, string? cardLast4, string? cardExpiry, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved the card (their token identity). Scopes all access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault payment token id — used as the payment source on future orders.</summary>
    public string PayPalVaultTokenId { get; private set; }

    /// <summary>The PayPal customer id these vaulted cards are grouped under for this shopper.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form (no full card data).</summary>
    public string? CardExpiry { get; private set; }

    /// <summary>Optional shopper-friendly label.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A safe, human-recognisable description, e.g. "VISA ****1111 (exp 2030-04)".</summary>
    public string Describe()
    {
        var brand = string.IsNullOrEmpty(CardBrand) ? "Card" : CardBrand;
        var last4 = string.IsNullOrEmpty(CardLast4) ? "????" : CardLast4;
        var expiry = string.IsNullOrEmpty(CardExpiry) ? string.Empty : $" (exp {CardExpiry})";
        return $"{brand} ****{last4}{expiry}";
    }
}
