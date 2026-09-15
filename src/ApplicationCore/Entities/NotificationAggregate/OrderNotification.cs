using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId);
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = Guard.Against.NullOrEmpty(content);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending_submission";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ContentDeletedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(ProviderMessage message, DateTimeOffset now)
    {
        ProviderMessageId = message.Id;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderErrorMessage = message.ErrorMessage;
        ProviderDateSent = message.DateSent;
        UpdatedAt = now;
    }

    public void RecordSubmissionFailure(int? errorCode, string? errorMessage, DateTimeOffset now)
    {
        ProviderStatus = "submission_failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = now;
    }

    public void DisposeContent(DateTimeOffset now)
    {
        Content = null;
        ContentDeletedAt = now;
        UpdatedAt = now;
    }
}
