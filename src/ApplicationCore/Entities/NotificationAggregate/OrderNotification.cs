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
        NotificationKind kind,
        string body,
        int? contactNumberId = null,
        DateTimeOffset? scheduledSendAt = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        ScheduledSendAt = scheduledSendAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }

    public void RecordProviderAccepted(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? "accepted" : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeError(errorMessage);
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderFailure(string status, int? httpStatus, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorCode = httpStatus;
        ProviderErrorMessage = SanitizeError(errorMessage);
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeError(errorMessage);
        LastProviderSyncAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (string.IsNullOrEmpty(body))
        {
            Body = null;
            ContentRedacted = true;
            return;
        }

        Body = body;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public bool IsScheduledFollowUp()
    {
        return Kind == NotificationKind.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        // Provider error text can echo the destination number; never persist it.
        return "Provider reported a delivery or send error.";
    }
}
