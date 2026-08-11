using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app keeps only
/// a reference to it (<see cref="PayPalVaultId"/>) plus the safe descriptors PayPal echoes back so the
/// shopper can recognise the card. The full PAN is never stored here. Scoped to its owner via
/// <see cref="BuyerId"/> so one shopper can never see, use, or delete another's card.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(
        string buyerId,
        string payPalVaultId,
        string? payPalCustomerId,
        string? brand,
        string? last4,
        string? expiry,
        string? cardholderName,
        string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        Alias = alias;
    }

    /// <summary>Owner of the card (the shopper's identity / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>Id of the vaulted card in PayPal (payment-token id). Used to charge the saved card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vaulted card is grouped under, if any.</summary>
    public string? PayPalCustomerId { get; private set; }

    // Safe, shopper-recognisable descriptors (never the full card number).
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    /// <summary>Optional shopper-chosen nickname for the card.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;
}
