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
        string? destination)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        Destination = destination;
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int? RelatedNotificationId { get; private set; }
    public string? Destination { get; private set; }

    public void RecordProviderAccepted(string providerSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledAt)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        ScheduledAt = scheduledAt;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = SanitizeError(errorMessage);
    }

    public void MarkSendFailed(string reason)
    {
        Status = "send_failed";
        ErrorMessage = SanitizeError(reason);
    }

    public void AttachRelated(int relatedNotificationId)
    {
        RelatedNotificationId = relatedNotificationId;
    }

    public void RedactLocalContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public static bool DidNotReachShopper(string status) =>
        status is "failed" or "undelivered" or "send_failed";

    public static bool IsStillQueuedAtProvider(string status) =>
        status is "scheduled" or "queued" or "accepted" or "sending";

    private static string? SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        // Provider error text can echo a destination number; keep a short, non-PII summary.
        return message.Length <= 200 ? message : message[..200];
    }
}
