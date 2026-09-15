using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        string destination, NotificationKind kind, string content,
        DateTimeOffset createdAt, DateTimeOffset? scheduledFor = null, int? originalNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId);
        ContactNumberId = contactNumberId;
        Destination = Guard.Against.NullOrEmpty(destination);
        Kind = kind;
        Content = Guard.Against.NullOrEmpty(content);
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ProviderStatus = "creating";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? ContentDeletedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public void ApplyProviderState(string sid, string status, int? errorCode,
        string? errorMessage, DateTimeOffset? sentAt, DateTimeOffset? updatedAt)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderSentAt = sentAt;
        ProviderUpdatedAt = updatedAt;
    }

    public void MarkProviderFailure(int? errorCode = null)
    {
        ProviderStatus = "provider-error";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = "The messaging provider did not accept the request.";
        ProviderUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDeleted(DateTimeOffset deletedAt)
    {
        Content = null;
        ContentDeletedAt = deletedAt;
    }
}
