using System;
using Ardalis.GuardClauses;
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
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKeyHash = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId);
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId);
        Kind = kind;
        Content = Guard.Against.NullOrEmpty(content);
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKeyHash = idempotencyKeyHash;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public string? ProviderMessageSid { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKeyHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void ApplyProviderState(
        string providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        DateTimeOffset? providerCreatedAt,
        DateTimeOffset? providerSentAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid);
        ProviderStatus = Guard.Against.NullOrEmpty(providerStatus);
        ProviderErrorCode = providerErrorCode;
        ProviderCreatedAt = providerCreatedAt;
        ProviderSentAt = providerSentAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProviderFailure(int? providerErrorCode = null)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = providerErrorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Content = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
