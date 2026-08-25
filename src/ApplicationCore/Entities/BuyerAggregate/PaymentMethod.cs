using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A shopper's saved card, represented only by what PayPal's Vault API returns about it.
/// The full card number and security code are never stored here or anywhere in this application.
/// </summary>
public class PaymentMethod : BaseEntity
{
    public int BuyerId { get; private set; }

    /// <summary>The PayPal-generated vault/payment-token id used to pay with this saved card.</summary>
    public string VaultId { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string Last4 { get; private set; } = null!;

    /// <summary>Card expiry in PayPal's "YYYY-MM" format.</summary>
    public string ExpiryYearMonth { get; private set; } = null!;
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(int buyerId, string vaultId, string brand, string last4, string expiryYearMonth,
        string? alias, DateTimeOffset createdAt)
    {
        Guard.Against.NegativeOrZero(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiryYearMonth, nameof(expiryYearMonth));

        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        ExpiryYearMonth = expiryYearMonth;
        Alias = alias;
        CreatedAt = createdAt;
    }

    /// <summary>A safe, human-recognisable description of the card, e.g. "VISA ending 1111 (expires 2028-04)".</summary>
    public string Describe() => $"{Brand} ending {Last4} (expires {ExpiryYearMonth})";
}
