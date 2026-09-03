using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        if (sourceNotificationId == null != string.IsNullOrWhiteSpace(resendIdempotencyKey))
        {
            throw new ArgumentException("A resend source and idempotency key must be supplied together.");
        }

        if (sourceNotificationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceNotificationId));
        }

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public OrderNotificationStatus Status { get; private set; } = OrderNotificationStatus.Pending;
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    public int? SourceNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? CancellationCompletedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }

    public bool IsContentDisposed => ContentDisposedAt != null;

    public void UpdateProviderState(
        string providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt,
        DateTimeOffset? providerUpdatedAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));

        if (ProviderMessageSid != null && !string.Equals(ProviderMessageSid, providerMessageSid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The provider message identifier cannot be changed.");
        }

        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ProviderCreatedAt = providerCreatedAt ?? ProviderCreatedAt;
        ProviderSentAt = providerSentAt ?? ProviderSentAt;
        ProviderUpdatedAt = providerUpdatedAt ?? ProviderUpdatedAt;

        if (Status is OrderNotificationStatus.Pending
            or OrderNotificationStatus.Failed
            or OrderNotificationStatus.OutcomeUnknown)
        {
            Status = OrderNotificationStatus.ProviderAccepted;
        }
    }

    public void RecordProviderFailure(
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset observedAt)
    {
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ProviderUpdatedAt = observedAt;

        if (Status is not OrderNotificationStatus.Canceled and not OrderNotificationStatus.CancellationPending)
        {
            Status = OrderNotificationStatus.Failed;
        }
    }

    public void RecordProviderOutcomeUnknown(
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset observedAt)
    {
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ProviderUpdatedAt = observedAt;

        if (Status is not OrderNotificationStatus.Canceled and not OrderNotificationStatus.CancellationPending)
        {
            Status = OrderNotificationStatus.OutcomeUnknown;
        }
    }

    public void RequestCancellation(DateTimeOffset requestedAt)
    {
        if (Status == OrderNotificationStatus.Canceled)
        {
            return;
        }

        CancellationRequestedAt ??= requestedAt;
        Status = OrderNotificationStatus.CancellationPending;
    }

    public void MarkCanceled(string? providerStatus, DateTimeOffset completedAt, DateTimeOffset? providerUpdatedAt = null)
    {
        CancellationRequestedAt ??= completedAt;
        CancellationCompletedAt = completedAt;
        ProviderStatus = providerStatus ?? ProviderStatus;
        ProviderUpdatedAt = providerUpdatedAt ?? ProviderUpdatedAt;
        Status = OrderNotificationStatus.Canceled;
    }

    public void MarkCancellationFailed(string? providerStatus, DateTimeOffset completedAt, DateTimeOffset? providerUpdatedAt = null)
    {
        CancellationRequestedAt ??= completedAt;
        CancellationCompletedAt = completedAt;
        ProviderStatus = providerStatus ?? ProviderStatus;
        ProviderUpdatedAt = providerUpdatedAt ?? ProviderUpdatedAt;
        Status = OrderNotificationStatus.CancellationFailed;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt ??= disposedAt;
    }
}
