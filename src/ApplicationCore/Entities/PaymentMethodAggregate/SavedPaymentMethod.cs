using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. The application stores only a safe description (brand,
/// last four digits, expiry) and the PayPal vault token — never full card details. Belongs to the shopper who
/// saved it (<see cref="BuyerId"/>); one shopper must never see, use, or delete another's.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string cardBrand, string lastFourDigits, string expiry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>The owning shopper's identity (the JWT username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault token used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
