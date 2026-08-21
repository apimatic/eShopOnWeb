using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card the shopper saved (vaulted at PayPal) for reuse on later orders. This app never stores full card
/// details — only the PayPal vault token id plus a safe descriptor (brand, last four digits, expiry, name)
/// so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string cardBrand, string lastFourDigits,
        string cardholderName, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardholderName = cardholderName;
        Expiry = expiry;
    }

    /// <summary>Owner of the saved card; one shopper must never see, use, or delete another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id — the only card reference kept, used to pay later.</summary>
    public string PayPalVaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string CardholderName { get; private set; }

    /// <summary>Card expiry in YYYY-MM form (for display only).</summary>
    public string Expiry { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
