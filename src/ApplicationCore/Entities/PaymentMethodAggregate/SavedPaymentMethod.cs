using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card the shopper has saved for reuse. Only a safe descriptor (brand, last four digits,
/// expiry, cardholder name) is stored locally; the full card lives exclusively in PayPal's vault,
/// referenced by <see cref="PayPalVaultId"/>.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? brand,
        string? lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastFourDigits { get; private set; }

    /// <summary>Human-readable expiry (e.g. "MM/YYYY"). Never the full card details.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
