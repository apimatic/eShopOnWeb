using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) about an order, together with the state the provider owns:
/// the message identifier and its current delivery outcome. That provider state is what lets a later
/// request act on the message (cancel, redact, resend) and report on it (reconciliation), not just the
/// request that created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        string buyerId,
        int orderId,
        NotificationType type,
        string toNumber,
        string body,
        bool isScheduled = false,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        SendState = NotificationSendState.Pending;
    }

    /// <summary>The shopper this message is about (scopes shopper reads).</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>E.164 destination. Personal data — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; null for messages that were not produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // ---- State the provider owns ----

    /// <summary>The provider's message identifier (SID). Needed to fetch status, cancel, redact.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery status (raw wire value, e.g. "delivered", "undelivered", "scheduled").</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    /// <summary>Provider-supplied error text. Never contains the destination number.</summary>
    public string? ProviderErrorMessage { get; private set; }

    public NotificationSendState SendState { get; private set; }

    /// <summary>The provider accepted the send and returned an identifier + initial status.</summary>
    public void RecordAccepted(string? sid, string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        ApplyProviderStatus(providerStatus, errorCode, errorMessage);
        if (SendState == NotificationSendState.Pending)
        {
            SendState = NotificationSendState.Accepted;
        }
    }

    /// <summary>The send was rejected by the provider (or never left) — the message did not go out.</summary>
    public void RecordSendFailure(int? errorCode, string? errorMessage)
    {
        SendState = NotificationSendState.Failed;
        if (errorCode is not null) ProviderErrorCode = errorCode;
        if (errorMessage is not null) ProviderErrorMessage = errorMessage;
    }

    /// <summary>The send outcome is indeterminate (a transport failure a duplicate-guard refused to retry).</summary>
    public void RecordSendIndeterminate(string? errorMessage)
    {
        SendState = NotificationSendState.Unknown;
        if (errorMessage is not null) ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh from a later fetch of the provider's status.</summary>
    public void UpdateDeliveryStatus(string? providerStatus, int? errorCode, string? errorMessage)
        => ApplyProviderStatus(providerStatus, errorCode, errorMessage);

    /// <summary>A scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
        SendState = NotificationSendState.Canceled;
    }

    /// <summary>Dispose of the message text locally. Provider-side redaction is done separately by the caller.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    private void ApplyProviderStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerStatus)) ProviderStatus = providerStatus;
        if (errorCode is not null) ProviderErrorCode = errorCode;
        if (errorMessage is not null) ProviderErrorMessage = errorMessage;

        var mapped = MapState(ProviderStatus);
        if (mapped is not null)
        {
            SendState = mapped.Value;
        }
    }

    // Wire status values are the provider's own (from the SDK contract sheet), not invented here.
    private static NotificationSendState? MapState(string? wire) => wire switch
    {
        "delivered" or "read" => NotificationSendState.Delivered,
        "failed" or "undelivered" => NotificationSendState.Failed,
        "canceled" => NotificationSendState.Canceled,
        "queued" or "sending" or "sent" or "accepted" or "scheduled"
            or "receiving" or "received" or "partially_delivered" => NotificationSendState.Accepted,
        _ => null
    };
}
