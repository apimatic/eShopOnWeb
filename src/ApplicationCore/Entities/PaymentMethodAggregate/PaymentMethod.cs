using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives in PayPal's vault; this app only
/// keeps the vault token and a safe, non-sensitive description (brand + last four + expiry) so the
/// shopper can recognise which card it is. No full card details are ever stored here.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owning shopper (the JWT identity). A card belongs to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge this card later.</summary>
    public string VaultId { get; private set; }

    /// <summary>PayPal customer id the vaulted card is associated with.</summary>
    public string? CustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastFourDigits { get; private set; }

    /// <summary>Expiry as PayPal reports it, "YYYY-MM".</summary>
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string vaultId, string? customerId, string brand,
        string lastFourDigits, string expiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        VaultId = Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        CustomerId = customerId;
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        LastFourDigits = Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
        CardholderName = cardholderName;
    }
}
