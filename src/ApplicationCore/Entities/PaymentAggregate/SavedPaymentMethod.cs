using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string paypalVaultId,
        string lastDigits,
        string brand,
        string? expiry,
        string? cardholderName,
        string? paypalCustomerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(paypalVaultId, nameof(paypalVaultId));
        Guard.Against.NullOrEmpty(lastDigits, nameof(lastDigits));

        BuyerId = buyerId;
        PayPalVaultId = paypalVaultId;
        LastDigits = lastDigits;
        Brand = string.IsNullOrWhiteSpace(brand) ? "UNKNOWN" : brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        PayPalCustomerId = paypalCustomerId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string LastDigits { get; private set; }
    public string Brand { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void EnsureOwnedBy(string buyerId)
    {
        if (IsDeleted || !string.Equals(BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new CheckoutException(404, "Payment method was not found.");
        }
    }

    public void MarkDeleted()
    {
        IsDeleted = true;
    }
}
