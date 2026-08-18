using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS the shop sent (or tried to send) to a shopper about one of their
/// orders. It carries enough of the state the provider owns — the provider's message identifier
/// and the current delivery outcome — that a later request can act on it (resend, cancel a
/// scheduled follow-up, dispose of its content) and report on it, not only the request that
/// created it.
/// The destination number and message body are stored so the message can be resent, but are
/// never written to logs.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Internal marker used when the provider never accepted the message at all.</summary>
    public const string StatusSendFailed = "send_failed";

    /// <summary>Internal marker for a message the shop intended to send but had no way to (no SID yet).</summary>
    public const string StatusUnknown = "unknown";

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = StatusUnknown;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity (username) of the shopper the message is about — used for ownership scoping.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination (canonical E.164) number. Stored for resend; never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of. Never logged.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (its SID). Null if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (wire value), or an internal marker.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When set, the provider is holding this message to send at this time (a scheduled follow-up).</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification (resend only).</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset? StatusUpdatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message: its SID and initial delivery status.</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? StatusUnknown : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        StatusUpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider never accepted the message (the underlying operation still succeeds).</summary>
    public void RecordSendFailed(string? reason)
    {
        DeliveryStatus = StatusSendFailed;
        ProviderErrorMessage = reason;
        StatusUpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Updates the last-known delivery outcome from a fresh read of the provider's state.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status))
            return;
        DeliveryStatus = status;
        if (errorCode.HasValue) ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ProviderErrorMessage = errorMessage;
        StatusUpdatedDate = DateTimeOffset.UtcNow;
    }

    public void MarkScheduled(string providerMessageSid, string status, DateTimeOffset sendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? StatusUnknown : status;
        ScheduledSendAt = sendAt;
        StatusUpdatedDate = DateTimeOffset.UtcNow;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Disposes of the message content locally, after it has been redacted at the provider.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        StatusUpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>True while the message is a scheduled follow-up the provider has not yet sent.</summary>
    public bool IsPendingScheduled =>
        Kind == NotificationKind.DeliveryFollowUp
        && ScheduledSendAt.HasValue
        && !string.IsNullOrEmpty(ProviderMessageSid)
        && !string.Equals(DeliveryStatus, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(DeliveryStatus, "sent", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(DeliveryStatus, "delivered", StringComparison.OrdinalIgnoreCase);
}
