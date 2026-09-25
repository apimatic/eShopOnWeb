using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper vaulted at PayPal and can reuse. The application stores only what safely identifies the
/// card (brand + last digits + expiry) and the PayPal vault token id — never the PAN, CVV or full number.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string? brand, string? lastDigits, string? expiry, string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper (JWT identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault (payment token) id, used as <c>card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id this card is vaulted under, reused across the shopper's cards.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }

    /// <summary>Expiry in <c>YYYY-MM</c> form (safe to show).</summary>
    public string? Expiry { get; private set; }

    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
