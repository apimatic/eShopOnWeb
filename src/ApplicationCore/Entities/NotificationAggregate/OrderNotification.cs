using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop raised for an order, and what became of it at the provider.
/// It carries enough provider-owned state (the message SID and current delivery status) that a
/// later request can act on it (cancel, resend, redact) and report on it — not only the one that
/// created it. The destination number and message body are treated as sensitive and never logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ProviderStatus = NotificationStatuses.Pending;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (username/email), for scoping reads to the caller's own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Sensitive; never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The shop's own copy of the message text. Nulled once the content is disposed. Sensitive; never logged.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SM…), once accepted. Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its wire status), or a synthetic value from <see cref="NotificationStatuses"/>.</summary>
    public string ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the message is a scheduled future send (the delivery follow-up), the time it is due.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this (resend) notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Record the outcome of an accepted send: the provider SID and its initial status.</summary>
    public void SetProviderResult(string? sid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? NotificationStatuses.Unknown : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Record a scheduled future send.</summary>
    public void SetScheduled(string? sid, DateTimeOffset sendAt)
    {
        ProviderMessageSid = sid;
        ProviderStatus = NotificationStatuses.Scheduled;
        ScheduledSendAt = sendAt;
    }

    /// <summary>Record that the send could not be submitted to the provider at all.</summary>
    public void MarkSendFailed(string? errorMessage)
    {
        ProviderStatus = NotificationStatuses.SendFailed;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh the delivery outcome from a later provider read or a cancel.</summary>
    public void UpdateStatus(string status, int? errorCode = null, string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        ProviderStatus = status;
        if (errorCode.HasValue)
        {
            ProviderErrorCode = errorCode;
        }
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ProviderErrorMessage = errorMessage;
        }
    }

    /// <summary>Dispose of the message content locally. The provider-side redaction is done by the caller first.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
