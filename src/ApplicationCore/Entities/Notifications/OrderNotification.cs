using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, NotificationKind kind, string body,
        DateTimeOffset createdAt, DateTimeOffset? scheduledFor = null, int? sourceNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public bool CancellationRequested { get; private set; }

    public void RecordProviderState(string providerMessageId, string status, int? errorCode,
        string? errorMessage, DateTimeOffset? dateSent)
    {
        ProviderMessageId = providerMessageId;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
    }

    public void RecordProviderState(string status, int? errorCode, string? errorMessage,
        DateTimeOffset? dateSent)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
    }

    public void RecordSendFailure(string reason)
    {
        ProviderStatus = "send_failed";
        ProviderErrorMessage = reason;
    }

    public void RequestCancellation() => CancellationRequested = true;

    public void MarkContentDisposed(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt ??= disposedAt;
    }
}
