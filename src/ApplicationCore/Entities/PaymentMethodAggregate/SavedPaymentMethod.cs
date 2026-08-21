using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved once for reuse. The application stores only the PayPal vault id and a
/// safe description (brand + last four + expiry) — never the full card number, which is held by
/// PayPal's vault, not this database.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string cardBrand, string lastFourDigits, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand ?? "UNKNOWN";
        LastFourDigits = lastFourDigits ?? string.Empty;
        Expiry = expiry ?? string.Empty;
    }

    /// <summary>Identity of the shopper who owns this saved card (the token's user name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault / payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry as PayPal reports it, e.g. "2030-01".</summary>
    public string Expiry { get; private set; }

    public DateTimeOffset SavedAt { get; private set; } = DateTimeOffset.UtcNow;
}
