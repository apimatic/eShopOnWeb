using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single text message the shop sent (or tried to send) about an order, together with enough of
/// the provider-owned state — the message identifier and its current delivery outcome — that a later
/// request can act on it (cancel, resend, redact) and report on it (reconciliation), not only the
/// request that first raised it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status used before any provider identifier exists for the message.</summary>
    public const string StatusPending = "pending";

    /// <summary>Local status used when the provider call itself failed and no message was accepted.</summary>
    public const string StatusSendFailed = "send_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity (user name) of the shopper the message is about.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in canonical E.164. Held so the message can be resent; never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID); null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...).</summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>When a follow-up is queued with the provider for later delivery, the instant it is due.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Provider-reported send time, when known.</summary>
    public DateTimeOffset? DateSent { get; private set; }

    /// <summary>Caller-supplied idempotency key of the resend request that produced this message, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message content has been redacted at the provider and cleared here.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the outcome of an accepted provider send/fetch.</summary>
    public void RecordProviderResult(string sid, string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent ?? DateSent;
    }

    /// <summary>Records that this message was accepted by the provider as a scheduled (future) send.</summary>
    public void RecordScheduled(string sid, string status, DateTimeOffset scheduledFor)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        DeliveryStatus = status;
        ScheduledFor = scheduledFor;
    }

    /// <summary>Records that the provider call failed outright, so no message was accepted.</summary>
    public void RecordSendFailed(string? errorMessage)
    {
        DeliveryStatus = StatusSendFailed;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent ?? DateSent;
    }

    public void SetIdempotencyKey(string key)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));
        IdempotencyKey = key;
    }

    /// <summary>Disposes of the message content locally; the provider copy is redacted separately.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
