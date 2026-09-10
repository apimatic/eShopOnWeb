using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The full card number and security code are never
/// stored here: only a PayPal vault token (<see cref="VaultId"/>) plus safe descriptors
/// (brand, last four digits, expiry) so the shopper can recognise the card. Belongs to the
/// shopper identified by <see cref="BuyerId"/>.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultId, string? customerId, string brand,
        string lastFourDigits, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        VaultId = vaultId;
        CustomerId = customerId;
        Brand = brand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string BuyerId { get; private set; }

    /// <summary>The PayPal payment-method token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>The PayPal vault customer id this card is filed under, if any.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in PayPal's "YYYY-MM" form.</summary>
    public string Expiry { get; private set; }

    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
