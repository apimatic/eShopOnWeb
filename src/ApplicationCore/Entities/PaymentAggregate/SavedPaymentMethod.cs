using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The sensitive card data lives only in PayPal's PCI-compliant
/// vault; this app stores just the vault token reference plus a safe descriptor (brand + last four +
/// expiry) that lets the shopper recognise the card. Full card details are never persisted here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string vaultId,
        string? vaultCustomerId,
        string cardBrand,
        string last4,
        int expiryMonth,
        int expiryYear,
        string cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.OutOfRange(expiryMonth, nameof(expiryMonth), 1, 12);

        BuyerId = buyerId;
        VaultId = vaultId;
        VaultCustomerId = vaultCustomerId;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CardholderName = cardholderName;
    }

    /// <summary>Identity of the shopper who owns this card. A card is only ever visible/usable to its owner.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The PayPal vault payment-token id used to charge this card later. Not sensitive card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>The PayPal customer id this card is grouped under, so a shopper's cards share one customer.</summary>
    public string? VaultCustomerId { get; private set; }

    public string CardBrand { get; private set; }
    public string Last4 { get; private set; }
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }
    public string CardholderName { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
}
