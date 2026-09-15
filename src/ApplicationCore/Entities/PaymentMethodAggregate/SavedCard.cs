using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted with PayPal) for reuse on later orders. Only a safe,
/// recognisable description is kept here plus the opaque PayPal vault token — never the full card
/// number, which is stored only inside PayPal's vault.
///
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>); ownership is always checked
/// before it is listed, used to pay, or deleted.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string provider, string payPalVaultId, string? payPalCustomerId,
        string brand, string last4, string expiryYearMonth, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(provider, nameof(provider));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        Provider = provider;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        ExpiryYearMonth = expiryYearMonth;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (their identity/username).</summary>
    public string BuyerId { get; private set; }

    public string Provider { get; private set; }

    /// <summary>The opaque PayPal vault token used to charge the card later. Not card data.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id the vault token is grouped under, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }

    /// <summary>The last four digits, safe to show so the shopper recognises the card.</summary>
    public string Last4 { get; private set; }

    /// <summary>The card expiry as year-month, e.g. "2030-01".</summary>
    public string ExpiryYearMonth { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A safe, human label the shopper can recognise, e.g. "Visa ending 1111".</summary>
    public string DisplayLabel => $"{Brand} ending {Last4}";
}
