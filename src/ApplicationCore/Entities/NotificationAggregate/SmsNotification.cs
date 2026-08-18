using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message the shop raised about an order, together with enough of the state the
/// provider owns (its message identifier and current delivery outcome) that a later request can
/// act on it — re-send it, cancel it, dispose of its content, or reconcile it — and report on it.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
#pragma warning restore CS8618

    public SmsNotification(
        string buyerId,
        int orderId,
        NotificationKind kind,
        string toNumber,
        string body,
        bool isFollowUp = false,
        DateTimeOffset? scheduledForUtc = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsFollowUp = isFollowUp;
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        DeliveryStatus = NotificationDeliveryStatus.NotSent;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    /// <summary>Owning shopper (the eShop user name / email).</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once an operator has disposed of the content.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (its message SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message. See <see cref="NotificationDeliveryStatus"/>.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>True for the "how did delivery go?" message queued with the provider for later.</summary>
    public bool IsFollowUp { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledForUtc { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator re-send (null for original messages).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a re-send, the notification whose message it re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message and now owns state for it.</summary>
    public void RecordAccepted(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? NotificationDeliveryStatus.Queued : status;
        ErrorCode = null;
        ErrorMessage = null;
        Touch();
    }

    /// <summary>Records that the provider never accepted the message (the create call failed).</summary>
    public void RecordSendFailure(string? errorMessage, int? errorCode = null)
    {
        DeliveryStatus = NotificationDeliveryStatus.NotSent;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        Touch();
    }

    /// <summary>Applies the latest delivery outcome read back from the provider.</summary>
    public void ApplyProviderState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            DeliveryStatus = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that a not-yet-sent scheduled message was called off before it went out.</summary>
    public void MarkScheduledCancelled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
        Touch();
    }

    /// <summary>
    /// Records that the message content has been disposed of. The text is cleared locally; the caller
    /// is responsible for having redacted it at the provider too. The record of the message and its
    /// outcome survives.
    /// </summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
