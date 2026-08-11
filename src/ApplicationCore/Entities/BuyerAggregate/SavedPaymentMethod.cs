using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper vaulted with PayPal so a later order can be paid without re-entering it.
/// The application's own database keeps only a safe descriptor (brand + last four + expiry) and
/// the PayPal vault id — never the PAN, security code, or any full card details.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper's identity (the JWT subject / user name), matching <c>Order.BuyerId</c>.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault id used as <c>payment_source.card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The per-shopper PayPal customer id the vault token is grouped under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string? brand, string? last4, string? expiry, string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardHolderName = cardHolderName;
    }
}
