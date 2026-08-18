using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop tried to send a shopper about an order. It carries enough
/// of the state the provider owns — the provider's message identifier and the current delivery
/// outcome — that a later request can act on the message (resend, dispose, cancel) and report on it,
/// not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status used before the provider has accepted the message.</summary>
    public const string StatusPending = "pending";

    /// <summary>Local status used when the provider never accepted the message (rejected, or unreachable).</summary>
    public const string StatusSendFailed = "send_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    private Notification(string ownerId, int orderId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static Notification Create(string ownerId, int orderId, NotificationKind kind, string toNumber, string body)
        => new(ownerId, orderId, kind, toNumber, body);

    /// <summary>The shopper the message is about / addressed to (their username).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The order this message concerns.</summary>
    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number (E.164). Persisted so the message can be resent; never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Nulled out once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message, once it has accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current view of what became of the message (its status string), or a local status.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True when this message was queued with the provider to be sent at a future time.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key, set on messages produced by an operator resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Records that the provider accepted the message for (immediate) sending.</summary>
    public void RecordAccepted(string sid, string providerStatus)
    {
        ProviderMessageSid = sid;
        DeliveryStatus = string.IsNullOrWhiteSpace(providerStatus) ? "accepted" : providerStatus;
        SentAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider accepted the message to be sent at a future time.</summary>
    public void RecordScheduled(string sid, string providerStatus, DateTimeOffset scheduledFor)
    {
        ProviderMessageSid = sid;
        DeliveryStatus = string.IsNullOrWhiteSpace(providerStatus) ? "scheduled" : providerStatus;
        IsScheduled = true;
        ScheduledFor = scheduledFor;
    }

    /// <summary>Records that the provider never accepted the message. Never fails the underlying operation.</summary>
    public void RecordSendFailure(int? errorCode, string? errorMessage)
    {
        DeliveryStatus = StatusSendFailed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Updates the stored delivery outcome from a fresh look at the provider's record.</summary>
    public void UpdateDeliveryStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
        {
            DeliveryStatus = providerStatus;
        }
        if (errorCode.HasValue)
        {
            ErrorCode = errorCode;
        }
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>Marks a scheduled message that was called off before it went out.</summary>
    public void MarkCancelled()
    {
        DeliveryStatus = "canceled";
        IsScheduled = false;
    }

    /// <summary>Disposes of the message content locally (the provider-side redaction is done by the caller).</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void AssignIdempotencyKey(string key)
    {
        IdempotencyKey = key;
    }

    /// <summary>Whether the provider might still change this message's outcome (so it is worth re-reading).</summary>
    public bool IsDeliveryOutcomePending()
    {
        if (ProviderMessageSid is null)
        {
            return false;
        }

        return DeliveryStatus switch
        {
            "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read" => false,
            _ => true
        };
    }
}
