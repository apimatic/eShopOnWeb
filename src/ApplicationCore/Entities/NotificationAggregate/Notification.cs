using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or tried to send) to a shopper about one order.
/// It carries enough of the state the provider owns — the provider message id
/// (<see cref="ProviderMessageId"/>) and the current delivery outcome
/// (<see cref="DeliveryStatus"/>/<see cref="ErrorCode"/>) — that a later request can act on it
/// (fetch/cancel/redact/resend) and report on it, not only the request that sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        DeliveryStatus = NotificationDeliveryStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner (the shopper the order and message belong to). Enforces per-shopper isolation.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (E.164). Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>Message text. Disposed (blanked) locally when a shopper asks for its content removed.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID), once the send has produced one.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>Last known delivery outcome. Provider statuses are stored verbatim; see <see cref="NotificationDeliveryStatus"/>.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When this is a follow-up, the time it is queued with the provider to go out.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider (redacted) and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Set on a message produced by a resend; ties the request to its outcome for replay.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    /// <summary>When this message is a resend, the id of the message it re-sent.</summary>
    public int? SourceNotificationId { get; private set; }

    public bool IsFollowUp => Kind == NotificationKind.DeliveryFollowUp;

    /// <summary>A follow-up the provider is still holding for future delivery and could still send.</summary>
    public bool IsPendingScheduledFollowUp =>
        IsFollowUp
        && ProviderMessageId is not null
        && DeliveryStatus == NotificationDeliveryStatus.Scheduled;

    public void RecordSent(string? providerMessageId, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageId = providerMessageId;
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? DeliveryStatus : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkScheduled(string? providerMessageId, DateTimeOffset scheduledFor)
    {
        ProviderMessageId = providerMessageId;
        ScheduledFor = scheduledFor;
        DeliveryStatus = NotificationDeliveryStatus.Scheduled;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
        ErrorMessage = errorMessage;
    }

    public void MarkCanceled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
    }

    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
            DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Disposes the message text locally, once the provider copy has been redacted.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void AttachResend(string idempotencyKey, int sourceNotificationId)
    {
        ResendIdempotencyKey = idempotencyKey;
        SourceNotificationId = sourceNotificationId;
    }
}
