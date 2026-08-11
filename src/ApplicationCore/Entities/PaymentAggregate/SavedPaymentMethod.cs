using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card a shopper saved (vaulted with PayPal) for reuse on later orders. The application's own
/// database never holds the full card number — only PayPal's vault identifier and safe descriptors
/// (brand, last four, expiry, cardholder name) so the shopper can recognise which card it is.
/// A saved card belongs to the shopper who saved it.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Owner's identity (the authenticated user name / token subject).</summary>
    public string BuyerId { get; private set; }

    /// <summary>PayPal vault (payment-token) id used to charge this card without re-entering it.</summary>
    public string VaultId { get; private set; }

    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? ExpiryMonth { get; private set; }
    public string? ExpiryYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultId, string? cardBrand, string? last4,
        string? expiryMonth, string? expiryYear, string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CardholderName = cardholderName;
    }
}
