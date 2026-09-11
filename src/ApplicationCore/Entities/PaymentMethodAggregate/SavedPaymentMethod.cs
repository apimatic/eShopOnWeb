using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper vaulted with PayPal for reuse. The application stores only a safe
/// description (brand, last four, expiry) plus PayPal's vault token id &mdash; never the PAN
/// or security code.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastDigits, string expiryMonthYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        ExpiryMonthYear = expiryMonthYear;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the card; used for shopper-scoped access checks.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id grouping this shopper's vaulted cards.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string ExpiryMonthYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
