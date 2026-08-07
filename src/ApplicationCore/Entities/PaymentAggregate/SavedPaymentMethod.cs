using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives only in PayPal's PCI-compliant
/// vault; we persist nothing but the PayPal token reference and a safe, human-recognisable
/// description (brand + last four + expiry). Full card details are never stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string payPalCustomerId,
        string cardBrand,
        string lastFourDigits,
        string cardExpiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
    }

    /// <summary>Identity of the shopper who owns this saved card (the JWT subject / user name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id, used as <c>vault_id</c> when charging.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id that groups all of this shopper's vaulted cards.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }

    /// <summary>Card expiry as reported by PayPal, in <c>YYYY-MM</c> form.</summary>
    public string? CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;
}
