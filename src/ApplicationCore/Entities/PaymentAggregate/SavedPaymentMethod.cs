using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives in PayPal's vault; this app keeps
/// only the vault token id plus a safe descriptor (brand, last 4, expiry) so the shopper can
/// recognise which card it is. Full card details are never stored here.
///
/// A saved card belongs to the shopper who saved it — access is always scoped by BuyerId.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper identity.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id, used as card.vault_id when paying.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the token is associated with (returned when vaulting).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM (as PayPal reports it). Not sensitive on its own.</summary>
    public string? ExpiryMonthYear { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? payPalCustomerId,
        string? cardBrand, string? cardLast4, string? expiryMonthYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        ExpiryMonthYear = expiryMonthYear;
        CardholderName = cardholderName;
    }
}
