using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destination,
        string? body,
        string? providerSid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? scheduledSendAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destination, nameof(destination));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ProviderSid = providerSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Destination { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool IsScheduled =>
        string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase);

    public bool DidNotReachShopper =>
        string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "undelivered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "send_failed", StringComparison.OrdinalIgnoreCase);

    public void ApplyProviderState(string? providerSid, string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        if (!string.IsNullOrEmpty(providerSid))
        {
            ProviderSid = providerSid;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (!ContentRedacted && body is not null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
