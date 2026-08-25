using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved to PayPal's vault for reuse. Only safe, non-sensitive details are
/// kept locally - the card number itself never touches this application.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalCustomerId, string payPalPaymentTokenId, string cardBrand, string lastDigits, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));
        Guard.Against.NullOrEmpty(payPalPaymentTokenId, nameof(payPalPaymentTokenId));

        BuyerId = buyerId;
        PayPalCustomerId = payPalCustomerId;
        PayPalPaymentTokenId = payPalPaymentTokenId;
        CardBrand = cardBrand ?? "UNKNOWN";
        LastDigits = lastDigits ?? string.Empty;
        Expiry = expiry ?? string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper - same string identity (username/email) used as Order.BuyerId.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault customer id this card is stored against.</summary>
    public string PayPalCustomerId { get; private set; }

    /// <summary>PayPal vault payment token id - passed as payment_source.card.vault_id to pay with this card.</summary>
    public string PayPalPaymentTokenId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastDigits { get; private set; }
    /// <summary>Expiry in "YYYY-MM" form, as reported by PayPal.</summary>
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
