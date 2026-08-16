using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper saved for reuse. The card itself lives in PayPal's vault — this app keeps only the
/// vault token (a PayPal reference, not card data) plus a safe descriptor (brand / last four / expiry).
/// Full card details are never stored here.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
    /// <summary>Owner of the card. A card belongs only to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal customer id that groups this shopper's vaulted cards.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card. Not card data.</summary>
    public string PaymentTokenId { get; private set; }

    public string Brand { get; private set; }
    public string Last4 { get; private set; }

    /// <summary>Card expiry in PayPal's <c>YYYY-MM</c> form.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string buyerId, string payPalCustomerId, string paymentTokenId,
        string brand, string last4, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(paymentTokenId, nameof(paymentTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PaymentTokenId = paymentTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
    }
}
