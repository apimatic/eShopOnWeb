using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app keeps only the
/// vault token plus safe display metadata (brand / last four / expiry) so the shopper can recognise it.
/// Full card details are never stored here. Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class CustomerPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private CustomerPaymentMethod() { }

    public CustomerPaymentMethod(string buyerId, string vaultId, string cardBrand, string last4,
        string? expiryMonth, string? expiryYear, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner's identity (the shopper's user name from the JWT). Scopes all access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault id / payment token used to charge this card later. Not card data.</summary>
    public string VaultId { get; private set; }

    public string? CardBrand { get; private set; }
    public string Last4 { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Human-friendly, safe label, e.g. "VISA ****1111".</summary>
    public string DisplayName => $"{(string.IsNullOrEmpty(CardBrand) ? "CARD" : CardBrand)} ****{Last4}";
}
