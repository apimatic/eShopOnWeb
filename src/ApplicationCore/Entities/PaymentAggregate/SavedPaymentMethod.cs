using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card vaulted at PayPal on behalf of a shopper. Only safe display data
/// (brand, last digits, expiry) is stored here - never the full card number or CVC.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() {}

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string brand, string lastDigits, string expiryMonth, string expiryYear)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string ExpiryMonth { get; private set; }
    public string ExpiryYear { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string Describe() => $"{Brand} ****{LastDigits} (expires {ExpiryMonth}/{ExpiryYear})";
}
