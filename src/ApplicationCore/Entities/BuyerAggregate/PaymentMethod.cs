using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper saved for reuse. The actual card data lives in PayPal's PCI-compliant vault;
/// this app only stores the PayPal vault token plus a safe descriptor (brand + last four + expiry)
/// so the shopper can recognise which card it is. Full card details are never persisted here.
/// A payment method belongs to exactly one buyer (<see cref="BuyerId"/>) and is only ever accessed
/// through specifications scoped to that buyer, so one shopper can never see another's cards.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string vaultId, string? cardBrand, string? lastFourDigits, string? cardholderName, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardholderName = cardholderName;
        Expiry = expiry;
        CreatedDate = DateTimeOffset.Now;
    }

    /// <summary>Owning shopper. Equal to the authenticated user's identity name.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal Vault payment-token id. This is the only reference used to charge the card.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network (e.g. VISA), for display only.</summary>
    public string? CardBrand { get; private set; }

    /// <summary>Last four digits of the PAN, for display only.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Cardholder name as returned by PayPal, for display only.</summary>
    public string? CardholderName { get; private set; }

    /// <summary>Expiry in YYYY-MM form, for display only.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
