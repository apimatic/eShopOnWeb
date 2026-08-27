using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public bool CancellationPending { get; private set; }
    public int CancellationAttempts { get; private set; }

    public void RecordProviderState(
        string sid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt,
        DateTimeOffset checkedAt)
    {
        ProviderMessageSid = sid;
        ApplyProviderState(status, errorCode, errorMessage, providerCreatedAt, providerSentAt, checkedAt);
    }

    public void RefreshProviderState(
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt,
        DateTimeOffset checkedAt) =>
        ApplyProviderState(status, errorCode, errorMessage, providerCreatedAt, providerSentAt, checkedAt);

    public void RecordSendFailure(int? errorCode, string errorMessage, DateTimeOffset checkedAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = LimitError(errorMessage);
        LastCheckedAt = checkedAt;
    }

    public void MarkCancellationPending()
    {
        CancellationPending = true;
        CancellationAttempts++;
    }

    public void MarkCancelled(DateTimeOffset checkedAt)
    {
        ProviderStatus = "canceled";
        CancellationPending = false;
        CancellationAttempts++;
        LastCheckedAt = checkedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Content = null;
        ContentDisposedAt ??= disposedAt;
    }

    private void ApplyProviderState(
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt,
        DateTimeOffset checkedAt)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = LimitError(errorMessage);
        ProviderCreatedAt = providerCreatedAt;
        ProviderSentAt = providerSentAt;
        LastCheckedAt = checkedAt;
        if (status == "canceled") CancellationPending = false;
    }

    private static string? LimitError(string? value) =>
        value is { Length: > 512 } ? value[..512] : value;
}
