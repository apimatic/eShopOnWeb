using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application database keeps only a safe descriptor
/// (brand, last four digits, expiry) plus the provider's vault token id — never full card details.
/// A saved card belongs to the shopper who saved it (<see cref="BuyerId"/>).
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string vaultId,
        string? customerId,
        string cardBrand,
        string lastFourDigits,
        string expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));

        BuyerId = buyerId;
        VaultId = vaultId;
        CustomerId = customerId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the card (the shopper's identity, taken from their token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal-generated customer id the token is associated with, if any.</summary>
    public string? CustomerId { get; private set; }

    public string CardBrand { get; private set; }

    /// <summary>Last four digits, safe to show so the shopper can recognise the card.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry in YYYY-MM form, safe to show.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
