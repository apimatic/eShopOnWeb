using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string requestId)
    {
        OwnerId = ownerId;
        RequestId = requestId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string RequestId { get; private set; } = string.Empty;
    public string? PayPalTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    public void Activate(string tokenId, string customerId, string? brand, string? lastDigits,
        string? expiry, string? cardType)
    {
        PayPalTokenId = tokenId;
        PayPalCustomerId = customerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardType = cardType;
    }

    public void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
