using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message the shop sent (or scheduled) about an order, together with the state the
/// provider owns for it: the provider's message identifier and the current delivery outcome. That
/// state is what lets a later request act on the message (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Provider status used when the message never reached the provider at all (local send failure).</summary>
    public const string StatusNotSubmitted = "not_submitted";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(
        string ownerId,
        int orderId,
        NotificationType type,
        string toPhoneNumberE164,
        string body,
        bool isScheduled,
        DateTimeOffset? scheduledFor,
        string? idempotencyKey,
        int? sourceNotificationId)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumberE164, nameof(toPhoneNumberE164));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Type = type;
        ToPhoneNumber = toPhoneNumberE164;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = StatusNotSubmitted;
    }

    /// <summary>Creates a notification to be sent to the shopper immediately.</summary>
    public static OrderNotification CreateImmediate(string ownerId, int orderId, NotificationType type,
        string toPhoneNumberE164, string body, string? idempotencyKey = null, int? sourceNotificationId = null)
        => new(ownerId, orderId, type, toPhoneNumberE164, body, isScheduled: false, scheduledFor: null,
            idempotencyKey, sourceNotificationId);

    /// <summary>Creates a notification the provider is asked to send at a future time.</summary>
    public static OrderNotification CreateScheduled(string ownerId, int orderId, NotificationType type,
        string toPhoneNumberE164, string body, DateTimeOffset scheduledFor)
        => new(ownerId, orderId, type, toPhoneNumberE164, body, isScheduled: true, scheduledFor: scheduledFor,
            idempotencyKey: null, sourceNotificationId: null);

    /// <summary>Identity of the shopper the message is about (the JWT subject / user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The order this message relates to.</summary>
    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Recipient number in E.164. Stored so the message can be resent; never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The text sent to the shopper. Becomes <c>null</c> once the content is disposed of (redacted).</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID), once it has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string ProviderStatus { get; private set; }

    /// <summary>The provider error code, when the message failed or was undelivered.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>True when the message content has been disposed of at the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>True when this message was queued with the provider to be sent later rather than immediately.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Caller-supplied idempotency key, for messages produced by a resend request.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification was produced by resending another, the id of that original notification.</summary>
    public int? SourceNotificationId { get; private set; }

    /// <summary>Records that the provider accepted the message, capturing its identifier and initial status.</summary>
    public void RecordSubmission(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = string.IsNullOrEmpty(providerStatus) ? "queued" : providerStatus;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void RecordSubmissionFailure() => ProviderStatus = StatusNotSubmitted;

    /// <summary>Refreshes the delivery outcome from a later reading of the provider's record.</summary>
    public void UpdateDeliveryState(string providerStatus, int? providerErrorCode)
    {
        if (!string.IsNullOrEmpty(providerStatus))
            ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>Records that a not-yet-sent scheduled message was called off before it went out.</summary>
    public void MarkCanceled() => ProviderStatus = "canceled";

    /// <summary>Disposes of the message content locally, after it has also been redacted at the provider.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>Whether the message is one that did not reach the shopper and is therefore a candidate for resend.</summary>
    public bool DidNotReachRecipient() => ProviderStatus is StatusNotSubmitted or "undelivered" or "failed" or "canceled";
}
