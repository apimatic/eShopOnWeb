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
        string? destinationNumber,
        int? contactNumberId,
        int? sourceNotificationId = null,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        SourceNotificationId = sourceNotificationId;
        ScheduledFor = scheduledFor;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? DestinationNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void RecordProviderAcceptance(string messageSid, string? status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void RecordSendFailure(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = Truncate(errorMessage, 500);
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = Truncate(errorMessage, 500);
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInFlight()
    {
        return string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "sending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "pending", StringComparison.OrdinalIgnoreCase);
    }

    public bool CanBeCancelledAtProvider()
    {
        return !string.IsNullOrEmpty(ProviderMessageSid)
            && (string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }
}
