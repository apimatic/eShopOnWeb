using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop tried to send to a shopper about one of their orders. It carries enough of
/// the state the provider owns — the provider message identifier and the current delivery outcome —
/// that a later request (resend, content disposal, reconciliation) can act on it and report on it,
/// not only the request that first sent it. The destination number and the message text are
/// sensitive and are never written to logs.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(
        string ownerId,
        int orderId,
        NotificationKind kind,
        string toNumber,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        SendState = NotificationSendState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Shopper the message is about (order BuyerId). Used to scope shopper-facing reads.</summary>
    public string OwnerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Sensitive — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared locally when content is disposed. Sensitive — never logged.</summary>
    public string? Body { get; private set; }

    public NotificationSendState SendState { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio message SID), once created.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome, stored verbatim (e.g. queued/sent/delivered/failed/undelivered/scheduled/canceled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The provider's send timestamp — the single clock reconciliation filters on (never the local row-creation time).</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>When a scheduled follow-up is due to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been disposed of, at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend (unique). Null for notifications not produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The provider accepted the message; capture its identifier and outcome.</summary>
    public void RecordSent(string? providerSid, string? providerStatus, DateTimeOffset? providerDateSent,
        int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerSid;
        ProviderStatus = providerStatus;
        ProviderDateSent = providerDateSent;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        SendState = NotificationSendState.Sent;
        Touch();
    }

    /// <summary>The provider refused the request — no message was created.</summary>
    public void RecordFailed(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        SendState = NotificationSendState.Failed;
        Touch();
    }

    /// <summary>The send could not be confirmed; the message may or may not have been created (reconcile).</summary>
    public void RecordUnknown(string? reason)
    {
        ProviderErrorMessage = reason;
        SendState = NotificationSendState.Unknown;
        Touch();
    }

    /// <summary>A scheduled message was called off before it went out.</summary>
    public void RecordCanceled(string? providerStatus)
    {
        ProviderStatus = providerStatus ?? ProviderStatus;
        SendState = NotificationSendState.Canceled;
        Touch();
    }

    /// <summary>Refresh the provider-owned state from a later read of the message resource.</summary>
    public void RefreshProviderState(string? providerStatus, DateTimeOffset? providerDateSent,
        int? errorCode, string? errorMessage)
    {
        if (providerStatus is not null) ProviderStatus = providerStatus;
        if (providerDateSent is not null) ProviderDateSent = providerDateSent;
        if (errorCode is not null) ProviderErrorCode = errorCode;
        if (errorMessage is not null) ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The message text has been disposed of at the provider; drop the local copy too.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    /// <summary>
    /// True when the provider's outcome indicates the message did not reach the shopper, so a resend is
    /// legitimate. Delivered/sent/received/read never qualify.
    /// </summary>
    public bool DidNotReachRecipient()
    {
        if (SendState is NotificationSendState.Failed or NotificationSendState.Unknown) return true;
        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
