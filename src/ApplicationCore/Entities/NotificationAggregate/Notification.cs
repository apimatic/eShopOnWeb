using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop tried to send to a shopper about one of their orders, together with
/// enough of the state the provider owns — its message identifier and current delivery outcome —
/// that a later request can act on it (re-send, redact, reconcile) and report on it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status used only when the provider never accepted the request (the create call itself failed).</summary>
    public const string SendErrorStatus = "send_error";

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(
        string buyerId,
        int orderId,
        NotificationKind kind,
        string toNumber,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? parentNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ParentNotificationId = parentNotificationId;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number (E.164). A shopper's number is never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The message text. Kept so an operator can re-send it. Set to <c>null</c> once the content has
    /// been disposed of; the record and its outcome survive redaction.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (the message SID). Null until the provider accepts it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current status for the message (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider (delivery follow-up only).</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator re-send. Null for messages that were not produced by a re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification a re-send was produced from, if any.</summary>
    public int? ParentNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Records that the provider accepted the create/schedule request.</summary>
    public void ApplyProviderResult(string providerMessageSid, string status, int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider never accepted the request. Never fails the underlying order operation.</summary>
    public void MarkSendFailed(string? errorMessage)
    {
        Status = SendErrorStatus;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Advances the stored delivery state from a fresh read of the provider's record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled()
    {
        Status = "canceled";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Disposes of the message content locally. The provider copy is redacted separately.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>Whether the stored status is a terminal delivery outcome that no longer needs to be re-read from the provider.</summary>
    public bool IsTerminalStatus() => Status is "delivered" or "undelivered" or "failed" or "canceled" or "read" or SendErrorStatus;

    /// <summary>Whether this message reached a state that means it did not get to the shopper (a candidate for re-send).</summary>
    public bool DidNotReachRecipient() => Status is "undelivered" or "failed" or SendErrorStatus;

    /// <summary>Whether this is a scheduled message the provider still holds and could yet send.</summary>
    public bool IsPendingScheduled() => Status == "scheduled";
}
