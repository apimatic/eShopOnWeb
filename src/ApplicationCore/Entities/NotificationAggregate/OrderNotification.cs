using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        int? contactNumberId,
        string? idempotencyKey = null,
        int? originalNotificationId = null,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordProviderAcceptance(string sid, string status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = null;
    }

    public void RecordProviderFailure(int? errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode, string? providerBody)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (ContentRedacted)
        {
            return;
        }

        if (providerBody != null && providerBody.Length == 0)
        {
            RedactLocalContent();
        }
    }

    public void RedactLocalContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsStillScheduled()
    {
        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase);
    }
}
