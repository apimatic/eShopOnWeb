using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop sent (or tried to send) about an order, together with enough of
/// the state the provider owns — the provider's message identifier and the current delivery
/// outcome — that a later request can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local channel-status sentinels used when the provider owns no status yet.</summary>
    public static class LocalStatus
    {
        /// <summary>The provider call failed; the message never left. The order operation still succeeded.</summary>
        public const string SendFailed = "send_failed";

        /// <summary>The follow-up was called off locally before it could be queued's cancel confirmed.</summary>
        public const string CancelRequested = "cancel_requested";
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ChannelStatus = string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper this message is about — used to scope shopper reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Nulled out once content has been disposed.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID), once the send was accepted.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The current delivery outcome: the provider's status string, or a <see cref="LocalStatus"/> sentinel.</summary>
    public string ChannelStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorDescription { get; private set; }

    /// <summary>True for the "how did delivery go" follow-up queued with the provider for later.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this notification was produced by an operator re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one re-sent, when produced by an operator re-send.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider (redacted) and locally.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Record that the provider accepted the send and owns the message under <paramref name="messageSid"/>.</summary>
    public void MarkAccepted(string messageSid, string channelStatus)
    {
        MessageSid = messageSid;
        ChannelStatus = string.IsNullOrEmpty(channelStatus) ? ChannelStatus : channelStatus;
        ErrorCode = null;
        ErrorDescription = null;
        Touch();
    }

    /// <summary>Record that the send could not be made — the order operation still succeeds.</summary>
    public void MarkSendFailed(string? reason)
    {
        ChannelStatus = LocalStatus.SendFailed;
        ErrorDescription = reason;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from what the provider now reports.</summary>
    public void UpdateDeliveryState(string? channelStatus, int? errorCode, string? errorDescription)
    {
        if (!string.IsNullOrEmpty(channelStatus))
        {
            ChannelStatus = channelStatus!;
        }
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
        Touch();
    }

    public void MarkAsScheduledFollowUp(DateTimeOffset sendAt)
    {
        IsScheduled = true;
        ScheduledSendAt = sendAt;
        Touch();
    }

    public void MarkAsResend(int resendOfNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    /// <summary>The content has been disposed: the local copy is dropped and the fact survives.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
