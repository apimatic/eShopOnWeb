using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. This is an aggregate root owned by exactly one shopper
/// (<see cref="OwnerId"/>) so ownership can be enforced independently of the payment processor.
///
/// Full card details are never stored here. The card itself lives in PayPal's PCI-compliant vault;
/// this entity only holds the PayPal-generated vault token (<see cref="VaultToken"/>) plus a safe,
/// non-sensitive description (brand / last four digits / expiry) so the shopper can recognise it.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(
        string ownerId,
        string vaultToken,
        string last4,
        string? brand,
        string? expiryMonthYear,
        string? cardholderName,
        string? alias)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(vaultToken, nameof(vaultToken));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        OwnerId = ownerId;
        VaultToken = vaultToken;
        Last4 = last4;
        Brand = brand;
        ExpiryMonthYear = expiryMonthYear;
        CardholderName = cardholderName;
        Alias = alias;
    }

    /// <summary>Identity of the shopper who saved (and therefore owns) this card.</summary>
    public string OwnerId { get; private set; }

    /// <summary>An optional, shopper-supplied friendly name (e.g. "personal Visa").</summary>
    public string? Alias { get; private set; }

    /// <summary>
    /// The PayPal vault token id. This is <b>not</b> card data — it is an opaque reference used to
    /// charge the vaulted card. Kept as the property name <c>CardId</c>'s successor per the original
    /// model's intent that "actual card data must be stored in a PCI compliant system".
    /// </summary>
    public string VaultToken { get; private set; }

    /// <summary>Last four digits of the card, safe to display.</summary>
    public string Last4 { get; private set; }

    /// <summary>Card network/brand (e.g. VISA), when known.</summary>
    public string? Brand { get; private set; }

    /// <summary>Card expiry in ISO-8601 <c>YYYY-MM</c> form, when known.</summary>
    public string? ExpiryMonthYear { get; private set; }

    /// <summary>Cardholder name as returned by PayPal, when known.</summary>
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;
}
