using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string payPalVaultId,
        string lastDigits,
        string brand,
        string expiry,
        string? cardholderName,
        string? payPalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalVaultId, nameof(payPalVaultId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));

        BuyerId = buyerId;
        PayPalVaultId = payPalVaultId;
        LastDigits = lastDigits;
        Brand = string.IsNullOrWhiteSpace(brand) ? "CARD" : brand;
        Expiry = expiry ?? string.Empty;
        CardholderName = cardholderName;
        PayPalCustomerId = payPalCustomerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);
}
