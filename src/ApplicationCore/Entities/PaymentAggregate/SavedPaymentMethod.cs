using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. This app never stores the card number or
/// security code: it keeps only PayPal's vault token id plus a safe descriptor (brand, last digits,
/// expiry) so the shopper can recognise which card it is.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper who owns this saved card. One shopper never sees another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal customer id under which this shopper's cards are vaulted.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>PayPal's vault payment-token id, used as <c>vault_id</c> when paying.</summary>
    public string VaultId { get; private set; }

    public string Brand { get; private set; }

    /// <summary>The last digits of the card (safe to show); never the full PAN.</summary>
    public string LastDigits { get; private set; }

    public string? CardholderName { get; private set; }

    /// <summary>Expiry in ISO year-month form, e.g. 2028-04.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalCustomerId, string vaultId, string brand,
        string lastDigits, string? cardholderName, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        VaultId = vaultId;
        Brand = string.IsNullOrEmpty(brand) ? "CARD" : brand;
        LastDigits = lastDigits ?? string.Empty;
        CardholderName = cardholderName;
        Expiry = expiry;
    }
}
