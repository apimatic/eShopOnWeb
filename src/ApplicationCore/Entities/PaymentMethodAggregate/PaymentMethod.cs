using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse. The application database never holds full card
/// details — only PayPal's vault token id plus a safe description (brand, last four, expiry) so the
/// shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string lastFourDigits, string? expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card. One shopper never sees, uses, or deletes another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the token is grouped under (for reuse across saves).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in PayPal's YYYY-MM form; safe to show.</summary>
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
