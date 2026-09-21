using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse on later orders. A saved card belongs to
/// the shopper who saved it (<see cref="BuyerId"/>) — one shopper never sees, uses, or deletes another's.
///
/// Full card details are never stored here: only PayPal's vault token id (<see cref="VaultId"/>) plus a
/// safe descriptor (brand + last four + expiry) so the shopper can recognise which card it is. The vault
/// id is what a later payment references to charge the saved card.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? brand, string? lastFourDigits, string? expiry, string? cardHolderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper's identity (the JWT subject / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card on future orders.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network/brand (e.g. VISA), for safe display only.</summary>
    public string? Brand { get; private set; }

    /// <summary>Last four digits of the card, for safe display only.</summary>
    public string? LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM, for safe display only.</summary>
    public string? Expiry { get; private set; }

    /// <summary>Cardholder name, for safe display only.</summary>
    public string? CardHolderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
