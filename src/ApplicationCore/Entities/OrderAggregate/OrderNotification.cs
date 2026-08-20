using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationE164,
        string body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        DateTimeOffset? sendAt,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationE164, nameof(destinationE164));
        Guard.Against.Null(body, nameof(body));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationE164 = destinationE164;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        SendAt = sendAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationE164 { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? bodyIfNotRedacted)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (!ContentRedacted && bodyIfNotRedacted is not null)
        {
            Body = bodyIfNotRedacted;
        }
    }

    public void MarkSendFailed(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
    }

    public void AssignProviderMessageSid(string sid)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsScheduledPending()
    {
        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase);
    }

    public bool DidNotReachShopper()
    {
        return string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase);
    }
}
