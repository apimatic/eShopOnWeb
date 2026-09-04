using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card a shopper saved to the PayPal vault for reuse on later orders.
/// No full card details are ever stored here — only the PayPal vault token id and
/// safe identifying data (last four digits, brand, expiry, cardholder name).
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; } = string.Empty;

    /// <summary>PayPal-generated customer id the token is attached to.</summary>
    public string PayPalCustomerId { get; private set; } = string.Empty;

    /// <summary>The PayPal vault payment-token id used to pay with this card.</summary>
    public string PayPalTokenId { get; private set; } = string.Empty;

    public string Last4 { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public string CardholderName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalCustomerId, string payPalTokenId,
        string last4, string brand, string expiry, string cardholderName)
    {
        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalTokenId = payPalTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}