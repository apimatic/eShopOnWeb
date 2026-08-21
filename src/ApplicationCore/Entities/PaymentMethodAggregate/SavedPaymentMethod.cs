using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved (vaulted at PayPal) for reuse on later orders. It holds only the PayPal
/// vault token id and a safe descriptor (brand, last digits, expiry) — never full card details.
/// A saved card belongs to exactly one shopper.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? brand, string? lastDigits,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>The shopper (username/email) that owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault / payment-method id used to pay with this card.</summary>
    public string VaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
