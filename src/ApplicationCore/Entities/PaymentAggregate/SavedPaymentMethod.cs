using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse (Flow 2). The application stores only a
/// safe description (brand, last digits, expiry) plus PayPal's vault token id — never the full
/// card number or CVV. Belongs to exactly one shopper.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string brand, string lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved (and solely owns) this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token id — used as <c>payment_source.card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id grouping this shopper's vaulted methods.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
