using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// One SMS message sent (or attempted) about an order, plus the provider state needed to act on it
/// and report on it later: the provider's message identifier and the current delivery outcome. The
/// record survives content disposal — only the body is redacted at the provider, never the fact
/// that a message was sent or what became of it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toPhoneNumber,
        bool isScheduled = false,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        IsScheduled = isScheduled;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner (token identity) — used to scope shopper-facing reads to their own orders.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (canonical E.164). Sensitive; never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The provider's unique identifier for the message (its SID). Null until a send succeeds.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (e.g. queued/sent/delivered/failed/undelivered/canceled),
    /// or a local marker such as <c>send_failed</c> when the request never reached the provider.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True for the scheduled delivery follow-up (queued with the provider for later).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message body has been redacted at the provider on a shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator re-send; unique so a repeat is not re-sent.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this is a re-send, the notification whose message it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Audit column: when the row was created. NOT used as the reconciliation clock.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>The message's send time (immediate send moment, or the scheduled send time). This is the
    /// clock reconciliation filters on, mirroring the provider's <c>DateSent</c>.</summary>
    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>Records the provider's response to a successful send/schedule.</summary>
    public void RecordSent(string? messageSid, string? providerStatus, DateTimeOffset sentAtUtc, int? errorCode, string? errorMessage)
    {
        MessageSid = messageSid;
        ProviderStatus = providerStatus;
        SentAtUtc = sentAtUtc;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Records that the send request itself failed (transport/provider rejection) without failing the
    /// underlying order operation. The row remains discoverable via reconciliation.</summary>
    public void RecordSendFailure(string? providerStatus, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the last-known delivery outcome from the provider.</summary>
    public void UpdateDeliveryOutcome(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkContentRedacted() => ContentRedacted = true;
}
