using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionRecordStatus
{
    Claimed,
    Creating,
    Succeeded,
    Unknown,
    Failed
}

public sealed class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    private SubscriptionRecord() { }

    public SubscriptionRecord(
        string userId,
        string productHandle,
        string normalizedProductHandle,
        string providerReference,
        DateTimeOffset now)
    {
        UserId = userId;
        ProductHandle = productHandle;
        NormalizedProductHandle = normalizedProductHandle;
        ProviderReference = providerReference;
        Status = SubscriptionRecordStatus.Claimed;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string NormalizedProductHandle { get; private set; } = string.Empty;
    public string ProviderReference { get; private set; } = string.Empty;
    public SubscriptionRecordStatus Status { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCreating(DateTimeOffset now)
    {
        Status = SubscriptionRecordStatus.Creating;
        FailureMessage = null;
        UpdatedAt = now;
    }

    public void MarkSucceeded(DateTimeOffset now)
    {
        Status = SubscriptionRecordStatus.Succeeded;
        FailureMessage = null;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionRecordStatus.Unknown;
        FailureMessage = null;
        UpdatedAt = now;
    }

    public void MarkFailed(string message, DateTimeOffset now)
    {
        Status = SubscriptionRecordStatus.Failed;
        FailureMessage = message;
        UpdatedAt = now;
    }
}
