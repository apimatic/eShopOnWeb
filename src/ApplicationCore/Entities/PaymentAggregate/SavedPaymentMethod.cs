using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal and can reuse for later orders. This app never stores
/// the card number or CVV — only the PayPal vault token id plus a safe descriptor (brand, last four
/// digits, expiry) so the shopper can recognise which card it is. Owned by <see cref="BuyerId"/>;
/// one shopper never sees, uses, or deletes another's.
/// </summary>
public class SavedPaymentMethod : IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastFourDigits, string expiryMonthYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        Id = Guid.NewGuid();
        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        ExpiryMonthYear = expiryMonthYear;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Primary key — the <c>paymentMethodId</c> returned to the caller.</summary>
    public Guid Id { get; private set; }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault token id used to fund a payment with this saved card.</summary>
    public string PayPalVaultId { get; private set; }

    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string ExpiryMonthYear { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
