using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse. This app stores only PayPal's vault token
/// and a safe description (brand, last four digits, expiry) — never the full card number.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? payPalCustomerId, string? brand,
        string? lastFourDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>The shopper who saved this card; only they may see, use or delete it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment token) id used to reference the card when paying.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal's owning-customer id for the vaulted card, replayed when paying with it.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }

    /// <summary>Last four digits, safe to show so the shopper can recognise the card.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Expiry in YYYY-MM form, as PayPal reports it.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
