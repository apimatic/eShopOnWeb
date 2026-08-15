using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on a later order. This app never stores
/// full card details — only the PayPal vault token and a safe description (brand + last four digits
/// + expiry) so the shopper can recognise which card it is. A saved card belongs to exactly one
/// shopper (<see cref="BuyerId"/>); no shopper may see, use or delete another's.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string lastFourDigits, string? expiryYearMonth, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        ExpiryYearMonth = expiryYearMonth;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper (their username/email, matching <see cref="OrderAggregate.Order.BuyerId"/>).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault (payment-token) id used to charge this card on a later order.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id the token is grouped under, if PayPal returned one.</summary>
    public string? PayPalCustomerId { get; private set; }

    /// <summary>Safe descriptor — card network (e.g. VISA).</summary>
    public string CardBrand { get; private set; }

    /// <summary>Safe descriptor — last four digits only.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Safe descriptor — expiry as YYYY-MM, if known.</summary>
    public string? ExpiryYearMonth { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
