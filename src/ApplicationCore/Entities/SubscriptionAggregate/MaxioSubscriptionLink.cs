using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioSubscriptionLink : BaseEntity, IAggregateRoot
{
    public const string PendingStatus = "Pending";
    public const string ConfirmedStatus = "Confirmed";
    public const string FailedStatus = "Failed";

    private MaxioSubscriptionLink() { }

    public MaxioSubscriptionLink(
        string userId,
        string productHandle,
        string pricePointHandle,
        string subscriptionReference,
        Guid leaseId,
        DateTimeOffset now)
    {
        UserId = userId;
        ProductHandle = productHandle;
        PricePointHandle = pricePointHandle;
        SubscriptionReference = subscriptionReference;
        Status = PendingStatus;
        LeaseId = leaseId;
        LeaseExpiresAt = now.AddSeconds(30);
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string PricePointHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; } = PendingStatus;
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? LastSafeErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public bool IsConfirmed => Status == ConfirmedStatus && MaxioSubscriptionId.HasValue;

    public bool TryAcquire(Guid leaseId, DateTimeOffset now)
    {
        if (IsConfirmed || Status == PendingStatus && LeaseExpiresAt > now)
        {
            return false;
        }

        Status = PendingStatus;
        LeaseId = leaseId;
        LeaseExpiresAt = now.AddSeconds(30);
        LastSafeErrorCode = null;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
        return true;
    }

    public void Confirm(Guid leaseId, int maxioSubscriptionId, DateTimeOffset now)
    {
        EnsureLease(leaseId);
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = ConfirmedStatus;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastSafeErrorCode = null;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Fail(Guid leaseId, string safeErrorCode, DateTimeOffset now)
    {
        EnsureLease(leaseId);
        Status = FailedStatus;
        LeaseId = null;
        LeaseExpiresAt = null;
        LastSafeErrorCode = safeErrorCode;
        UpdatedAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    private void EnsureLease(Guid leaseId)
    {
        if (LeaseId != leaseId)
        {
            throw new InvalidOperationException("The subscription operation lease is no longer owned by this request.");
        }
    }
}
