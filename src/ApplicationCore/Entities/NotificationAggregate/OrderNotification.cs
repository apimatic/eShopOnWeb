using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string? body,
        string? destinationPhoneNumber,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null,
        string? resendIdempotencyKey = null,
        OrderNotificationKind? originalKind = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationPhoneNumber = destinationPhoneNumber;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        OriginalKind = originalKind;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public OrderNotificationKind? OriginalKind { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? DestinationPhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(string messageSid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderOutcome(status, errorCode);
    }

    public void RecordProviderFailure(int? errorCode, string status = "failed")
    {
        ApplyProviderOutcome(status, errorCode);
    }

    public void ApplyProviderOutcome(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
