using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted) with PayPal for reuse. The application database never
/// stores the card number — only PayPal's vault token id and a safe descriptor (brand, last four
/// digits, expiry) so the shopper can recognise which card it is.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
    #pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string lastFourDigits, string? expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedDate = DateTimeOffset.Now;
    }

    /// <summary>The shopper who owns this saved card (their username). Used to scope every access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token id used to pay with this card. Not a card number.</summary>
    public string VaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in "YYYY-MM" form, as returned by PayPal's vault descriptor.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
