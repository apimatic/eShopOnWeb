using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message raised for an order. It records both what eShop asked the
/// provider to send and the state the provider owns for it — the provider's message
/// identifier (<see cref="ProviderMessageSid"/>) and the current delivery outcome
/// (<see cref="Status"/>) — so a later request can act on it (cancel, resend, redact)
/// and report on it without being the request that sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toPhoneNumber,
        string body,
        bool isScheduled = false,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = NotificationStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the notification (the shopper the message is about).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (E.164). PII — never written to logs or responses' plain text.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>Message text. Null once the content has been redacted/disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (Twilio Message SID). Null until accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// Current delivery outcome. While unsent this is <see cref="NotificationStatus.Pending"/>;
    /// otherwise it mirrors the provider's status verbatim (queued, sending, sent, delivered,
    /// undelivered, failed, scheduled, canceled, ...).
    /// </summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the follow-up message queued with the provider to go out days later.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is the product of a resend, the id of the original.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Records the provider's response to the send/schedule request.</summary>
    public void RecordProviderResult(string? providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        UpdateStatus(status, errorCode, errorMessage);
    }

    /// <summary>Records that the provider could not be reached at all (no message identifier exists).</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        UpdateStatus(NotificationStatus.Failed, null, errorMessage);
    }

    /// <summary>Refreshes the delivery outcome from a later fetch of the provider's record.</summary>
    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        Status = string.IsNullOrWhiteSpace(status) ? Status : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the local record as content-disposed. The text no longer survives here.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>True when the message reached (or is en route to) the recipient rather than having failed.</summary>
    public bool ReachedRecipient() => NotificationStatus.HasReachedRecipient(Status);

    /// <summary>True when the message ended in a state where a resend is warranted.</summary>
    public bool CanBeResent() => NotificationStatus.IsUndelivered(Status);
}
