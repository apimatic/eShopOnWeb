using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives only in PayPal's vault:
/// this aggregate stores the vault token id plus a safe descriptor (brand + last four +
/// expiry) so the shopper can recognise the card. No full card number is ever stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultTokenId, string payPalCustomerId,
        string brand, string lastFourDigits, string expiry, string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultTokenId, nameof(payPalVaultTokenId));

        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of this saved card (the shopper's username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card later.</summary>
    public string PayPalVaultTokenId { get; private set; }

    /// <summary>PayPal customer id the vaulted card is associated with.</summary>
    public string PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }

    /// <summary>Last four digits, safe to display.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in PayPal's YYYY-MM form, safe to display.</summary>
    public string Expiry { get; private set; }

    public string CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
