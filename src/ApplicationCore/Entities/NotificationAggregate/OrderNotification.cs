using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) to a shopper about one order, together with the
/// provider state needed to act on it and report on it later: the provider's message identifier
/// and its current delivery outcome. The destination number and body are stored so the message
/// can be re-sent and reconciled, but the number is never written to logs, and the body can be
/// disposed of on request.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string ownerId, string toPhoneNumber, NotificationKind kind,
        string body, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        ToPhoneNumber = toPhoneNumber;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        Status = MessageStatuses.SendError; // until a provider result is recorded
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Username of the shopper who owns the order (used to scope reads).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Canonical E.164 destination. Never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider (Twilio) message SID. Null if the provider call never produced one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current provider delivery outcome (see <see cref="MessageStatuses"/>).</summary>
    public string Status { get; private set; }

    /// <summary>Provider error code for a failed/undelivered message, when supplied.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Set for a message produced by a resend request; de-duplicates repeat requests.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this message re-sends an earlier one, the id of that earlier message.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>When set, the message is a scheduled (future) send with the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Records the result of a successful provider create/schedule call.</summary>
    public void SetProviderResult(string providerMessageSid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? MessageStatuses.Queued : status;
        ErrorCode = errorCode;
    }

    /// <summary>Records that the provider call failed locally and no message was created.</summary>
    public void SetSendFailed(int? errorCode = null)
    {
        Status = MessageStatuses.SendError;
        ErrorCode = errorCode;
    }

    /// <summary>Refreshes the delivery outcome from the provider's latest view of the message.</summary>
    public void UpdateStatus(string status, int? errorCode)
    {
        if (!string.IsNullOrEmpty(status))
            Status = status;
        if (errorCode.HasValue)
            ErrorCode = errorCode;
    }

    public void MarkCanceled() => Status = MessageStatuses.Canceled;

    public void SetIdempotencyKey(string key) => IdempotencyKey = key;

    public void SetResendOf(int originalNotificationId) => ResendOfNotificationId = originalNotificationId;

    /// <summary>Clears the stored body once it has been disposed of at the provider.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>True if this message did not reach the shopper and so is eligible for re-send.</summary>
    public bool IsDeliveryFailure() => MessageStatuses.IsFailure(Status);
}
