using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A card the shopper vaulted with PayPal for reuse. Only safe display data is
/// stored (last digits, brand, expiry) plus PayPal's token ids - never the PAN or CVC.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string buyerId, string payPalCustomerId, string paymentTokenId,
        string? lastDigits, string? brand, string? expiry)
    {
        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PaymentTokenId = paymentTokenId;
        LastDigits = lastDigits;
        Brand = brand;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    /// <summary>PayPal's customer id for this shopper (needed to list vaulted tokens).</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>PayPal vault payment-token id, used as payment_source.token.id when paying.</summary>
    public string PaymentTokenId { get; private set; }

    public string? LastDigits { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
