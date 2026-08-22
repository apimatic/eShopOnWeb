using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string NotSentStatus = "not_sent";

    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        int? contactNumberId,
        DateTimeOffset? scheduledSendAt = null,
        int? parentNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        ScheduledSendAt = scheduledSendAt;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = NotSentStatus;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset? LastSyncedUtc { get; private set; }

    public void ApplyProviderAcceptance(string sid, string status, int? errorCode, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed()
    {
        ProviderStatus = "failed";
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode, DateTimeOffset? dateSent, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastSyncedUtc = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (body is not null)
        {
            Body = body;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
