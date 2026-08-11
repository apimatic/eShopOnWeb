using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted with PayPal) for reuse on later orders. The
/// application stores only the PayPal vault token and a safe descriptor of the card —
/// never the full card number, which lives only in PayPal's vault.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string last4, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (their token identity / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault payment-token id used to charge the card later.</summary>
    public string VaultId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>Last four digits, safe to show so the shopper can recognise the card.</summary>
    public string Last4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
