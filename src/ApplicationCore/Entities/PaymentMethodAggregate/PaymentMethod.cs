using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. This app never keeps
/// the card number: it stores only PayPal's vault token id plus a safe descriptor (brand + last
/// four + expiry) so the shopper can recognise which card it is. Belongs to exactly one shopper.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string lastFourDigits, string? expiry, string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper (username) who owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment-token) id used to charge the card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vaulted card is attached to, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }

    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM form, if PayPal returned it.</summary>
    public string? Expiry { get; private set; }

    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Human-friendly, safe description, e.g. "VISA ****1111".</summary>
    public string Describe() => $"{CardBrand} ****{LastFourDigits}";
}
