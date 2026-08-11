using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The full card details are never stored here; only PayPal's vault token
/// id and a safe descriptor (brand, last four digits, expiry) the shopper can use to recognise the card.
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string vaultId,
        string? payPalCustomerId,
        string brand,
        string lastDigits,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        VaultId = vaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper who owns this saved card (the token subject / order buyer id).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal payment-method (vault) token id used to charge the card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the token is attached to (if any).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
