using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The full card details are never stored here — they live
/// only inside PayPal's PCI-compliant Vault. This entity keeps the opaque PayPal Vault token id
/// (used to charge the card later) plus a small, safe description (brand + last four digits +
/// expiry) so the shopper can recognise which card it is.
///
/// Modelled as its own aggregate root and owned by exactly one buyer (<see cref="BuyerId"/>), so
/// one shopper can never see, use or delete another shopper's saved card.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(
        string buyerId,
        string vaultTokenId,
        string cardBrand,
        string last4,
        string expiry,
        string? cardHolderName,
        string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultTokenId, nameof(vaultTokenId));

        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        Alias = alias;
        CreatedDate = DateTimeOffset.Now;
    }

    /// <summary>Identity of the shopper who owns this saved card (the token subject / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal Vault payment-token id used to charge this card. Not card data.</summary>
    public string VaultTokenId { get; private set; }

    /// <summary>Card network, e.g. VISA — safe to display.</summary>
    public string? CardBrand { get; private set; }

    /// <summary>Last four digits of the PAN — safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Expiry in PayPal's YYYY-MM form — safe to display.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Card holder name as reported by PayPal — safe to display.</summary>
    public string? CardHolderName { get; private set; }

    /// <summary>Optional shopper-supplied nickname, e.g. "personal visa".</summary>
    public string? Alias { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
