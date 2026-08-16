using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper vaulted at PayPal for reuse. The application database only ever
/// holds a safe descriptor (brand, last four digits, expiry) plus PayPal's vault
/// token id — never the full card number.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string cardLast4, string cardExpiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        PayPalCustomerId = payPalCustomerId;
        CardBrand = Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        CardLast4 = Guard.Against.NullOrEmpty(cardLast4, nameof(cardLast4));
        CardExpiry = Guard.Against.NullOrEmpty(cardExpiry, nameof(cardExpiry));
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the card (the shopper's username/buyer id). Enforced on every access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault v3 payment-token id used to charge the card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vaulted card is grouped under.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string CardLast4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form (safe to display).</summary>
    public string CardExpiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Safe, shopper-facing description of the card.</summary>
    public string Describe() => $"{CardBrand} ****{CardLast4}";
}
