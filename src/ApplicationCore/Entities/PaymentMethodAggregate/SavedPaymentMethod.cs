using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) for reuse. This app stores only a safe description of
/// the card plus PayPal's vault token — never the full card number.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string? payPalCustomerId,
        string cardBrand,
        string lastFourDigits,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.Now;
    }

    /// <summary>Owner of the card (the identity username). Scopes all access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id used to charge the card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the token is associated with (for reuse across orders).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Expiry in "YYYY-MM" form (safe to show).</summary>
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
