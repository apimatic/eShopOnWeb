using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string providerTokenId, string? providerCustomerId,
        string? cardholderName, string? brand, string? lastDigits, string? expiry,
        string? cardType, string? verificationStatus)
    {
        OwnerId = ownerId;
        ProviderTokenId = providerTokenId;
        ProviderCustomerId = providerCustomerId;
        CardholderName = cardholderName;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardType = cardType;
        VerificationStatus = verificationStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string ProviderTokenId { get; private set; } = string.Empty;
    public string? ProviderCustomerId { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public string? VerificationStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted() => DeletedAt = DateTimeOffset.UtcNow;
}
