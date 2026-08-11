using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application never stores full card details — only
/// PayPal's vault token (used to charge later) and a safe descriptor so the shopper can
/// recognise which card it is. A saved card belongs to exactly one shopper.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultId, string? payPalCustomerId,
        string cardBrand, string lastFourDigits, string cardExpiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the saved card. Only this shopper may see, use, or delete it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id — used to charge the card later. Not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the vault token is grouped under, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>Last four digits only — safe to show, never the full number.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry as reported by PayPal (e.g. "2027-02").</summary>
    public string CardExpiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
