using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. The full card number and CVV are never stored here or
/// anywhere in this application's database — only PayPal's vault token id and a safe descriptor
/// (brand, last four digits, expiry) that lets the shopper recognise which card it is.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalVaultId, string? brand, string? last4, string? expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the card (the buyer identity / username from the JWT).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token id used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form (as returned by PayPal).</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
