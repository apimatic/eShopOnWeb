using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The sensitive card data lives only in PayPal's PCI-compliant
/// vault; this entity holds a reference to the PayPal payment token plus a safe descriptor
/// (brand / last four digits / expiry) so the shopper can recognise the card. Full card details are
/// never stored here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string paymentTokenId,
        string cardBrand,
        string lastFourDigits,
        string cardExpiry,
        string? cardholderName,
        string? providerCustomerId)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PaymentTokenId = Guard.Against.NullOrEmpty(paymentTokenId, nameof(paymentTokenId));
        CardBrand = Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        LastFourDigits = Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        CardExpiry = Guard.Against.NullOrEmpty(cardExpiry, nameof(cardExpiry));
        CardholderName = cardholderName;
        ProviderCustomerId = providerCustomerId;
    }

    /// <summary>The identity (username/email) of the shopper who owns this card. Enforces per-shopper isolation.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal Vault v3 payment-token id used to charge this card. Not card data.</summary>
    public string PaymentTokenId { get; private set; }

    /// <summary>Card network, e.g. VISA. Safe to display.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the card. Safe to display.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM form. Safe to display.</summary>
    public string CardExpiry { get; private set; }

    /// <summary>Cardholder name as supplied when saving. Safe to display.</summary>
    public string? CardholderName { get; private set; }

    /// <summary>The PayPal-generated customer id the token is associated with, if any.</summary>
    public string? ProviderCustomerId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;
}
