using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. The application database never
/// holds the card number — only a safe descriptor and PayPal's vault token id, which is what lets a later
/// order be paid without re-entering the card.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(
        string buyerId,
        string payPalVaultId,
        string? payPalCustomerId,
        string cardBrand,
        string lastFourDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who saved this card (their username). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault/payment-token id — referenced as the payment source when paying with this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal's customer id the token is grouped under, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    /// <summary>Card brand (e.g. VISA) — safe to show.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits — safe to show so the shopper recognises the card.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry (YYYY-MM), if PayPal returned it — safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Cardholder name, if supplied — safe to show.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
