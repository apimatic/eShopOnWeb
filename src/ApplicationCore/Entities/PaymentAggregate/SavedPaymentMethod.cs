using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved (vaulted at PayPal) for reuse. This app never stores full card details:
/// only PayPal's vault id plus a safe descriptor (brand + last digits + expiry) so the shopper can
/// recognise which card it is. A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string? payPalCustomerId,
        string? brand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(payPalVaultId, nameof(payPalVaultId));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        PayPalCustomerId = payPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of this saved card (the authenticated shopper who saved it).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal's vault (payment-token) id used to pay with this card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>The PayPal customer id the card is vaulted under (for grouping a shopper's cards).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
