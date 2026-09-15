using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public sealed class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string requestId)
    {
        OwnerId = ownerId;
        CreateRequestId = requestId;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string CreateRequestId { get; private set; } = string.Empty;
    public string? PayPalTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public string? VerificationStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsActive => DeletedAt is null && PayPalTokenId != null;

    public void Activate(string tokenId, string? customerId, string? brand, string? lastDigits,
        string? expiry, string? cardType, string? verificationStatus)
    {
        PayPalTokenId = tokenId;
        PayPalCustomerId = customerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardType = cardType;
        VerificationStatus = verificationStatus;
    }

    public void Delete() => DeletedAt = DateTimeOffset.UtcNow;
}
