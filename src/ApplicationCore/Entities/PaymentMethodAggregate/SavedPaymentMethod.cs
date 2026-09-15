using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's Vault — this app only keeps
/// the vault token id plus a safe description (brand, last four digits, expiry) so the shopper can
/// recognise which card it is. Full card details are never stored here or anywhere in this app's database.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string payPalCustomerId,
        string cardBrand,
        string lastFourDigits,
        string cardExpiry,
        string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(payPalCustomerId, nameof(payPalCustomerId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The identity of the shopper who owns this saved card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id the vault token is grouped under.</summary>
    public string PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? LastFourDigits { get; private set; }
    public string? CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A safe, shopper-recognisable label such as "Visa ending 1111 (exp 2030-01)".</summary>
    public string DisplayName
    {
        get
        {
            var brand = string.IsNullOrEmpty(CardBrand) ? "Card" : CardBrand;
            var tail = string.IsNullOrEmpty(LastFourDigits) ? string.Empty : $" ending {LastFourDigits}";
            var exp = string.IsNullOrEmpty(CardExpiry) ? string.Empty : $" (exp {CardExpiry})";
            return $"{brand}{tail}{exp}";
        }
    }
}
