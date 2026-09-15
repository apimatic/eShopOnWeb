using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The record of one SMS the shop sent (or tried to send, or scheduled) about an order.
///
/// It carries enough of the state the provider owns — its message identifier and the current
/// delivery outcome — that a later request can act on it (re-send, cancel a schedule, redact the
/// body) and report on it, independent of the request that first created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>App-side status used when a message never reached the provider at all (no SID).</summary>
    public const string NotSentStatus = "not_sent";

    // Provider delivery statuses that will never change again — no point re-querying the provider for these.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read", NotSentStatus
    };

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, string toNumber, NotificationKind kind, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this notification is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (their identity/username). Scopes shopper-facing reads.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The destination number (provider-canonical E.164). Persisted, but never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Cleared once the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio message SID). Null if it never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last delivery outcome we know of — a provider status wire value, or <see cref="NotSentStatus"/>.</summary>
    public string? Status { get; private set; }

    /// <summary>Provider error code for an undeliverable/failed message, when the provider supplies one.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>Provider (or app-side) explanation for a failure/undeliverable outcome.</summary>
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when this message is held and sent by the provider at a future time (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for the operator re-send that produced this message.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When we last reconciled this record's status against the provider.</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Whether the provider still owns changeable state for this message that is worth re-querying.</summary>
    public bool IsPending() =>
        ProviderMessageSid is not null &&
        (Status is null || !TerminalStatuses.Contains(Status));

    /// <summary>Record the outcome of a successful hand-off to the provider (immediate send or fetch).</summary>
    public void RecordProviderResult(string providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record a message the provider is holding to send later (the delivery follow-up).</summary>
    public void RecordScheduled(string providerMessageSid, string? status, DateTimeOffset sendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        IsScheduled = true;
        ScheduledSendAt = sendAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the message could not be handed to the provider at all — it was never sent.</summary>
    public void RecordSendFailed(string? errorMessage)
    {
        Status = NotSentStatus;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery outcome we last read from the provider.</summary>
    public void RefreshStatus(string? status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark that the provider has cancelled this (scheduled) message before it was sent.</summary>
    public void MarkCanceled()
    {
        Status = "canceled";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record the idempotency key under which an operator re-send produced this message.</summary>
    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Dispose of the message content locally once it has also been redacted at the provider.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
