using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse. The application stores only PayPal's vault
/// id plus a safe description (brand + last four + expiry) — never the full card number.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string payPalVaultId, string? payPalCustomerId,
        string cardBrand, string cardLast4, string? cardExpiry, string? cardholderName)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PayPalVaultId = Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        PayPalCustomerId = payPalCustomerId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning shopper (eShop BuyerId). A saved card belongs only to the shopper who saved it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal-generated vault (payment-token) id used to pay with the saved card.</summary>
    public string PayPalVaultId { get; private set; }

    /// <summary>PayPal customer id grouping this shopper's vaulted cards (if PayPal assigned one).</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
