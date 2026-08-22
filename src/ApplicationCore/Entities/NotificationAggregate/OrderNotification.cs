using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string NotSentStatus = "not_sent";
    public const string LocalFailureStatus = "send_failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationNumber,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resentFromNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResentFromNotificationId = resentFromNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = NotSentStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = NotSentStatus;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public bool HasProviderIdentity => !string.IsNullOrWhiteSpace(ProviderMessageSid);

    public bool IsPendingWithProvider =>
        HasProviderIdentity && IsPendingStatus(ProviderStatus);

    public bool DidNotReachShopper =>
        string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase);

    public void RecordProviderResult(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            ProviderMessageSid = sid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RecordSendFailure()
    {
        ProviderStatus = LocalFailureStatus;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public string? BodyForDisplay => ContentRedacted ? null : Body;

    public static bool IsPendingStatus(string? status) =>
        string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase);
}
