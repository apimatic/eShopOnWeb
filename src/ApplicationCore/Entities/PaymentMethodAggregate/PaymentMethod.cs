using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The card itself lives only in PayPal's vault
/// (<see cref="PayPalVaultId"/>); this app stores no card number — only a safe descriptor
/// (brand + last four + expiry) so the shopper can recognise which card it is.
///
/// A saved card belongs to exactly one shopper (<see cref="BuyerId"/>); one shopper must never
/// see, use or delete another's.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string cardLastFour, string cardExpiry, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLastFour = cardLastFour;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper (the token identity that saved the card).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal payment-token id used to charge this card later.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal-generated customer id the vaulted card is associated with.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string CardLastFour { get; private set; }
    public string CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
