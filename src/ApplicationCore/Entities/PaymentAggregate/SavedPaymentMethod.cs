using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved once (vaulted at PayPal) and can reuse to pay later orders.
/// Only PayPal's vault token and safe descriptors are kept here — never the PAN or CVV.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastFourDigits, string expiry, string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id, used as the card reference when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vault token is grouped under, reused for later saves.</summary>
    public string? PayPalCustomerId { get; private set; }

    /// <summary>Card network, e.g. VISA. Safe to show.</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits only, so the shopper can recognise the card.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Expiry in YYYY-MM form.</summary>
    public string Expiry { get; private set; }

    public string CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
