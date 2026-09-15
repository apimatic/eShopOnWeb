using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string kind,
        string body, DateTimeOffset createdAt, DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Kind = Guard.Against.NullOrEmpty(kind, nameof(kind));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(string providerMessageId, string status, int? errorCode,
        DateTimeOffset? dateCreated, DateTimeOffset? dateSent)
    {
        ProviderMessageId = providerMessageId;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
    }

    public void MarkProviderFailure(int? errorCode)
    {
        ProviderStatus = "provider-error";
        ProviderErrorCode = errorCode;
    }

    public void MarkCancellationPending() => ProviderStatus = "cancel-pending";

    public void DisposeContent(DateTimeOffset now)
    {
        Body = null;
        ContentDisposedAt = now;
    }
}
