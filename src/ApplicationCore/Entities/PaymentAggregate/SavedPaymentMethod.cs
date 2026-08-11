using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) with PayPal for reuse on later orders. The application
/// database never holds the card number — only the PayPal-issued vault token and a safe
/// description (brand + last four digits + expiry) so the shopper can recognise the card.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string cardBrand,
        string lastDigits, string? expiryYearMonth, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        ExpiryYearMonth = expiryYearMonth;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card (username/email); scopes access to the caller.</summary>
    public string BuyerId { get; private set; } = default!;

    /// <summary>The PayPal vault token id, used to charge the card on later orders.</summary>
    public string PayPalVaultId { get; private set; } = default!;

    public string CardBrand { get; private set; } = default!;

    /// <summary>Last four digits only — enough to recognise the card, never the full PAN.</summary>
    public string LastDigits { get; private set; } = default!;

    public string? ExpiryYearMonth { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
