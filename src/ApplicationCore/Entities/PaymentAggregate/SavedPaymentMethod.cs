using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card vaulted at PayPal by a shopper. Only safe display data (brand, last digits,
/// expiry) is stored here; full card details never touch the database.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalCustomerId, string vaultTokenId,
        string brand, string lastFourDigits, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string PayPalCustomerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }
    /// <summary>Card expiry in PayPal's YYYY-MM format.</summary>
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
