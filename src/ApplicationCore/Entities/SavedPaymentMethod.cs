using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card the shopper saved for later use. Only safe display metadata is stored locally;
/// the card itself lives in the processor's vault, referenced by the vault token id.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultTokenId, string? cardBrand, string? lastDigits,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));

        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        CardBrand = cardBrand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultTokenId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? LastDigits { get; private set; }

    /// <summary>Card expiry in PayPal's YYYY-MM format.</summary>
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
