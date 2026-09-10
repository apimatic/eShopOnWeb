using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved once and can reuse for later orders. Full card details are never stored:
/// only PayPal's vault token and a safe, recognisable description (brand, last digits, expiry) are kept.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultToken, string? payPalCustomerId,
        string brand, string lastDigits, string? expiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultToken, nameof(vaultToken));

        PaymentMethodId = Guid.NewGuid().ToString("N");
        BuyerId = buyerId;
        VaultToken = vaultToken;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The opaque, caller-facing id for this saved card.</summary>
    public string PaymentMethodId { get; private set; }

    /// <summary>The shopper who owns this card.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault token — the only thing that can later fund a payment with this card.</summary>
    public string VaultToken { get; private set; }

    /// <summary>The PayPal customer id the card is vaulted under (groups a shopper's cards).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
