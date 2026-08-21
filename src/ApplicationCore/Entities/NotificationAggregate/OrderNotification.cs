using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        int? contactNumberId,
        string? destinationNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledAt = sendAt;
    }

    public void AssignResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void ApplyProviderState(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkLocalFailure(string status, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsPendingWithProvider()
    {
        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "sending", StringComparison.OrdinalIgnoreCase);
    }
}
