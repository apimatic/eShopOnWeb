using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// A record of one SMS the shop sent (or tried to send) about an order. It carries enough of the
/// state the provider owns — its message SID and the last known delivery outcome — that a later
/// request can act on it (fetch/refresh, resend, redact, reconcile) and report on it.
/// The message text is never stored here; it is regenerated deterministically from
/// <see cref="Type"/> + <see cref="OrderId"/>, so disposing of the provider's copy leaves no
/// second copy behind. The destination number is stored (needed to resend) but never logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Lifecycle status used before/without a provider send.</summary>
    public const string StatusNotSent = "not_sent";        // shopper has no number on file
    public const string StatusSendFailed = "send_failed";  // the send call itself failed
    public const string StatusCancelFailed = "cancel_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toPhoneNumber,
        bool isScheduledFollowUp = false, string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        IsScheduledFollowUp = isScheduledFollowUp;
        IdempotencyKey = idempotencyKey;
        Status = StatusNotSent;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (token username). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio message SID), once sent.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its status string) or an app lifecycle status.</summary>
    public string Status { get; private set; } = StatusNotSent;

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the follow-up queued with the provider for a few days later.</summary>
    public bool IsScheduledFollowUp { get; private set; }

    /// <summary>True once the provider's copy of the content has been disposed of.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedDate { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>Record a successful (accepted/queued/scheduled) provider send.</summary>
    public void RecordSent(string messageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? providerDateSent)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = string.IsNullOrEmpty(status) ? "unknown" : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = providerDateSent;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the send could not be performed (the order op still succeeds).</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = StatusSendFailed;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Update the last known delivery outcome from a provider fetch.</summary>
    public void UpdateStatus(string status, int? errorCode, string? errorMessage, DateTimeOffset? providerDateSent)
    {
        if (!string.IsNullOrEmpty(status)) Status = status;
        if (errorCode.HasValue) ErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ErrorMessage = errorMessage;
        if (providerDateSent.HasValue) ProviderDateSent = providerDateSent;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    public void MarkCancelFailed()
    {
        Status = StatusCancelFailed;
        UpdatedDate = DateTimeOffset.UtcNow;
    }
}
