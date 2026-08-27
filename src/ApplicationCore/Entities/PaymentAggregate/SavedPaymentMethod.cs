using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card vaulted at PayPal for a shopper. Only safe display attributes are stored here;
/// full card details never enter this database.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string? payPalCustomerId, string vaultTokenId,
        string? cardBrand, string? lastDigits, string? expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        VaultTokenId = Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
