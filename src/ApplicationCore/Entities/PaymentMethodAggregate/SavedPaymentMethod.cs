using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper saved for reuse. Only vault-safe details are kept — the PayPal vault-token id plus
/// a description safe enough for the shopper to recognise the card (brand + last four + expiry).
/// Full card details are never stored here.
///
/// A saved card belongs to the shopper who saved it (<see cref="BuyerId"/>); one shopper must never
/// see, use, or delete another's.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string brand, string last4,
        string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (their username/email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault-token id used to charge the card later. Not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network, e.g. VISA.</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits, safe to show.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry as YYYY-MM, if PayPal returned it.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Cardholder name as entered, if any.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
