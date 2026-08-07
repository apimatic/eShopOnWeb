using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's Vault;
/// this record holds only the vault reference plus the safe, non-sensitive details
/// needed to show the shopper which card it is. No PAN, no CVC, ever.
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
        string last4,
        string expiry,
        string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    /// <summary>The owning shopper (the username carried on their JWT).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal Vault payment-token id used to charge this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id that groups this shopper's vaulted cards.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>Card network, e.g. "VISA". Safe to display.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the card. Safe to display.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry in YYYY-MM. Safe to display.</summary>
    public string Expiry { get; private set; }

    /// <summary>Cardholder name as entered. Safe to display.</summary>
    public string CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;
}
