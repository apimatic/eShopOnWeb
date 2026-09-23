using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A shopper's vaulted card. The application stores only a safe descriptor (brand, last four, expiry,
/// cardholder name) plus the PayPal vault token id — never the PAN, CVV, or full card details.
/// A saved card belongs to the shopper who saved it; every read/use/delete is scoped by <see cref="BuyerId"/>.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault token id, used as <c>card.vault_id</c> to pay with the saved card.</summary>
    public string PayPalVaultTokenId { get; private set; }

    /// <summary>PayPal customer id these tokens group under (for merchant-side bookkeeping).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalVaultTokenId, string? payPalCustomerId,
        string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));
        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
