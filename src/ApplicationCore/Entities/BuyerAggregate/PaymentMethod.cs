using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The actual card data lives in PayPal's vault (a
/// PCI-compliant system) and is referenced here only by its vault token; this app never stores
/// the card number. A payment method belongs to the shopper who saved it.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the shopper who owns this saved card (the token's subject / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>A friendly, shopper-chosen name for the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id — the only reference to the underlying card.</summary>
    public string CardId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultToken, string? cardBrand, string? last4,
        string? expiryMonth, string? expiryYear, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultToken, nameof(vaultToken));
        BuyerId = buyerId;
        CardId = vaultToken;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Alias = alias;
    }
}
