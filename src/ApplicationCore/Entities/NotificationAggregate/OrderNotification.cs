using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        int? contactNumberId = null,
        string? idempotencyKey = null,
        int? sourceNotificationId = null,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ContactNumberId = contactNumberId;
        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        ScheduledSendAt = scheduledSendAt;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordProviderAccepted(string messageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = messageSid;
        ApplyProviderOutcome(status, errorCode, errorMessage);
    }

    public void RecordSendFailure(string status, int? errorCode, string? errorMessage)
    {
        ApplyProviderOutcome(string.IsNullOrWhiteSpace(status) ? "failed" : status, errorCode, errorMessage);
    }

    public void ApplyProviderOutcome(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsPendingWithProvider()
    {
        return ProviderStatus is "pending" or "accepted" or "queued" or "scheduled" or "sending";
    }
}
