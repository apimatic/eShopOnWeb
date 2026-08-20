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
        OrderNotificationKind kind,
        string destination,
        string body,
        string? providerMessageSid,
        string status,
        int? errorCode,
        DateTimeOffset? scheduledSendAt,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destination, nameof(destination));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ScheduledSendAt = scheduledSendAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Destination { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? providerBody, string? providerMessageSid)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (providerBody != null)
        {
            Body = providerBody.Length == 0 ? null : providerBody;
            if (providerBody.Length == 0)
            {
                ContentRedacted = true;
            }
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        var status = ProviderStatus.ToLowerInvariant();
        return status is "failed" or "undelivered" or "canceled";
    }
}
