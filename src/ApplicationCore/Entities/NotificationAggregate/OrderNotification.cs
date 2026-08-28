using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, int contactNumberId, NotificationKind kind, string content,
        DateTimeOffset createdAt, DateTimeOffset? scheduledFor = null, int? resendsNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendsNotificationId = resendsNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ProviderDateUpdated { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ResendsNotificationId { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? CancellationCompletedAt { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode, DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent, DateTimeOffset? dateUpdated)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        FailureReason = null;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        ProviderDateUpdated = dateUpdated;
    }

    public void RecordFailure(string safeReason, int? providerErrorCode = null)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = providerErrorCode;
        FailureReason = safeReason;
    }

    public void RequestCancellation(DateTimeOffset at)
    {
        CancellationRequestedAt ??= at;
    }

    public void CompleteCancellation(string status, DateTimeOffset at)
    {
        ProviderStatus = status;
        CancellationCompletedAt = at;
        FailureReason = null;
    }

    public void DisposeContent(DateTimeOffset at)
    {
        Content = null;
        ContentDisposedAt = at;
    }
}
