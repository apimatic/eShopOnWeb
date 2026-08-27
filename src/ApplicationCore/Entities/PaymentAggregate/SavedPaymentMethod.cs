using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card vaulted with PayPal. Only safe display data (brand, last digits, expiry) is kept
/// locally; the full card details live exclusively in PayPal's vault.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() {}

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string? brand, string? lastFourDigits, string? expiryMonth, string? expiryYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
