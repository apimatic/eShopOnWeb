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
        int? contactNumberId,
        string destinationNumber,
        NotificationPurpose purpose,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Purpose = purpose;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "not_sent";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public NotificationPurpose Purpose { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? SendFailureReason { get; private set; }

    public void RecordScheduledFor(DateTimeOffset sendAt) => ScheduledFor = sendAt;

    public void RecordResend(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void RecordProviderAccepted(string messageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void RecordSendFailure(string reason)
    {
        SendFailureReason = reason;
        ProviderStatus = "send_failed";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DestinationIsStillRegistered(int? activeContactNumberId) =>
        ContactNumberId.HasValue && activeContactNumberId == ContactNumberId;
}
