using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives only in PayPal's vault; this record keeps
/// the vault token plus a safe, recognisable description (brand and last four digits) — never the PAN
/// or CVV, which are never stored in this application's database.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string payPalCustomerId,
        string brand, string lastFourDigits, string expiry, string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this card. One shopper never sees, uses or deletes another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used as <c>payment_source.card.vault_id</c> when paying.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vault token belongs to (reused when the shopper saves more cards).</summary>
    public string PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in <c>YYYY-MM</c> form, safe to show.</summary>
    public string Expiry { get; private set; }
    public string CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
