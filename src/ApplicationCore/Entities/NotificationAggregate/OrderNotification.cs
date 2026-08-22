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
        string body,
        string destinationNumber,
        bool isScheduled)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        IsScheduled = isScheduled;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = isScheduled ? "scheduled" : "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsScheduled { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public void MarkScheduledFor(DateTimeOffset sendAt)
    {
        ScheduledFor = sendAt;
        IsScheduled = true;
        Status = "scheduled";
    }

    public void MarkAsResendOf(int originalNotificationId)
    {
        OriginalNotificationId = originalNotificationId;
    }

    public void RecordProviderAcceptance(string sid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void RecordLocalSendFailure(string errorMessage)
    {
        Status = "failed";
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            IsScheduled = false;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(Status, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTerminalFailure()
    {
        return string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "undelivered", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanBeCancelledAtProvider()
    {
        return IsScheduled
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && (string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Status, "accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Status, "queued", StringComparison.OrdinalIgnoreCase));
    }
}
