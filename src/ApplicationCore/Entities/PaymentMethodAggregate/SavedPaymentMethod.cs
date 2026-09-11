using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. The application's own
/// database never holds full card details — only a safe descriptor (brand, last digits, expiry)
/// and the PayPal vault token id used to charge it. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string? payPalCustomerId,
        string brand, string lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (JWT identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault payment-token id; used as <c>card.vault_id</c> to charge later.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>PayPal vault customer id this token is grouped under, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    /// <summary>Card network/brand (e.g. VISA) — safe to show.</summary>
    public string Brand { get; private set; }

    /// <summary>Last digits of the card — safe to show.</summary>
    public string LastDigits { get; private set; }

    /// <summary>Expiry in YYYY-MM, if PayPal returned it — safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Card holder name as PayPal returned it — safe to show.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
