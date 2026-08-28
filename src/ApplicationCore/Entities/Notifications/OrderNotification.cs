using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string userId,
        NotificationKind kind,
        string content,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        UserId = Guard.Against.NullOrEmpty(userId, nameof(userId));
        Kind = kind;
        Content = Guard.Against.NullOrEmpty(content, nameof(content));
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        Outcome = scheduledFor.HasValue ? "pending_schedule" : "pending_send";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public string Outcome { get; private set; } = string.Empty;
    public bool CancellationRequested { get; private set; }

    public bool IsContentDisposed => ContentDisposedAt.HasValue;
    public bool IsScheduled => ScheduledFor.HasValue;

    public void RecordProviderState(
        string? providerSid,
        string? providerStatus,
        int? providerErrorCode,
        DateTimeOffset? providerDateCreated,
        DateTimeOffset? providerDateSent)
    {
        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            ProviderSid = providerSid;
        }

        ProviderStatus = providerStatus ?? ProviderStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderDateCreated = providerDateCreated ?? ProviderDateCreated;
        ProviderDateSent = providerDateSent ?? ProviderDateSent;
        LastRefreshedAt = DateTimeOffset.UtcNow;
        Outcome = providerStatus ?? (ProviderSid is null ? Outcome : "accepted");
    }

    public void RecordFailure(string outcome, int? providerErrorCode = null)
    {
        Outcome = Guard.Against.NullOrEmpty(outcome, nameof(outcome));
        ProviderErrorCode = providerErrorCode;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    public void RequestCancellation()
    {
        CancellationRequested = true;
        Outcome = "cancellation_pending";
    }

    public void RecordCancellation()
    {
        CancellationRequested = false;
        ProviderStatus = "canceled";
        Outcome = "canceled";
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Content = null;
        ContentDisposedAt = DateTimeOffset.UtcNow;
    }
}
