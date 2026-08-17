using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved for reuse. The application stores only a safe, non-sensitive
/// descriptor plus PayPal's vault token id — never the card number, expiry secret, or CVC.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string vaultId,
        string? customerId,
        string brand,
        string lastFourDigits,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        VaultId = vaultId;
        CustomerId = customerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper (ASP.NET Identity user name / email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal Vault payment-token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal Vault customer id this card is filed under, if any.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Non-secret expiry as returned by PayPal, e.g. "2027-01".</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Shopper-recognisable, safe description. Never full card details.</summary>
    public string Description => $"{Brand} ****{LastFourDigits}";
}
