using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. The application stores only PayPal's vault
/// token id plus a safe descriptor (brand, last four, expiry) — never the full card number.
/// Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand,
        string lastFourDigits, string expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner identity (the shopper username from the token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault / payment-token id used to pay with this card.</summary>
    public string VaultId { get; private set; }

    public string CardBrand { get; private set; }
    public string LastFourDigits { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
