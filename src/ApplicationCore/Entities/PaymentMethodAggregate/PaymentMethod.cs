using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. The app stores
/// only a safe description of the card (brand + last four + expiry) plus PayPal's vault token
/// id — never the full card number or CVC. Belongs to exactly one shopper.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string payPalVaultTokenId, string cardBrand,
        string lastFourDigits, string? cardholderName, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardholderName = cardholderName;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>The shopper who saved this card (their identity handle / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token id — used as the payment source when paying with this card.</summary>
    public string PayPalVaultTokenId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>The last four digits, safe to show so the shopper can recognise the card.</summary>
    public string LastFourDigits { get; private set; }

    public string? CardholderName { get; private set; }

    /// <summary>Card expiry in "YYYY-MM" form. Not sensitive on its own.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
