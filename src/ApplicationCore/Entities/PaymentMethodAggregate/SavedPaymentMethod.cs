using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string requestId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(requestId, nameof(requestId));
        BuyerId = buyerId;
        CreateRequestId = requestId;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string CreateRequestId { get; private set; } = string.Empty;
    public string? PayPalVaultId { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? Type { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void RecordVault(string vaultId, string? name, string? brand, string? lastDigits, string? expiry, string? type)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        PayPalVaultId = vaultId;
        CardholderName = name;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        Type = type;
    }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
