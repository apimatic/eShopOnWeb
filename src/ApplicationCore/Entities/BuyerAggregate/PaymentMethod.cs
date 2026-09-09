using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app keeps
/// only the vault token id and a safe description (brand, last four, expiry) — never full card
/// details. Belongs to the shopper who saved it (<see cref="OwnerId"/>); one shopper must never
/// see, use, or delete another's.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string vaultTokenId, string? brand, string? last4,
        string? expiry, string? cardHolderName, string? alias)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        OwnerId = ownerId;
        VaultTokenId = vaultTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper who owns this saved card (the token subject / username).</summary>
    public string OwnerId { get; private set; }

    /// <summary>PayPal vault payment-token id; used as the payment source when paying with this card.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>Card network as PayPal reported it (e.g. VISA). Safe to show.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits, so the shopper can recognise the card. Safe to show.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry (YYYY-MM) as PayPal reported it. Safe to show.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Cardholder name as supplied when vaulting. Safe to show.</summary>
    public string? CardHolderName { get; private set; }

    /// <summary>Optional shopper-chosen label.</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
