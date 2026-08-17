using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application database only ever holds the PayPal vault token and a
/// safe descriptor (brand, last four digits, expiry) — never the full card number or security code.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastFourDigits, string expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        PayPalCustomerId = payPalCustomerId;
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        LastFourDigits = Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal Vault payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in PayPal's "YYYY-MM" form.</summary>
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
