using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application stores only a safe descriptor plus the
/// PayPal vault token that stands in for the card — never the card number, expiry secret, or CVC.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string? payPalCustomerId,
        string? cardBrand,
        string? last4,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this card (their token identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card again.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id that groups this shopper's vaulted cards.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
