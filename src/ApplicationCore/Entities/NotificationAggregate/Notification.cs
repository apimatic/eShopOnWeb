using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS an order raised for a shopper, together with enough of the state the provider
/// owns (its message identifier and current delivery outcome) that a later request can act on
/// it and report on it — not only the request that first sent it.
/// The destination number and message body are treated as sensitive and are never logged.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    public string OwnerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in E.164. Sensitive — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared locally once the shopper asks for its content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (Twilio Sid). Null when the provider never accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Latest known delivery outcome: a provider status wire value, or a local <see cref="NotificationDeliveryStatus"/>.</summary>
    public string DeliveryStatus { get; private set; } = NotificationDeliveryStatus.Pending;

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True for the "how did delivery go?" follow-up the provider holds to send later.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; null for notifications not produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(
        string ownerId,
        int orderId,
        NotificationKind kind,
        string toNumber,
        string body,
        bool isScheduled = false,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>The provider accepted the message: record its Sid and the status it reported.</summary>
    public void RecordAccepted(string? providerMessageSid, string deliveryStatus, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The provider refused the message at send time; no Sid was issued.</summary>
    public void RecordSendFailed(int? errorCode, string? errorMessage)
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's own current record.</summary>
    public void UpdateDeliveryStatus(string deliveryStatus, int? errorCode, string? errorMessage)
    {
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>A scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
        Touch();
    }

    /// <summary>Dispose of the message content locally. The record of what was sent and its outcome survives.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
