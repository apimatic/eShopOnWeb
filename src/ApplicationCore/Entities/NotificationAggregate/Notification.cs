using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one SMS this application sent (or tried to send) to a shopper for an order.
/// It carries enough of the state the provider owns — the message <see cref="ProviderSid"/> and its
/// current delivery <see cref="Status"/> — that a later request can act on it (cancel, resend,
/// dispose of its content) and report on it, not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        CreatedDate = DateTimeOffset.UtcNow;
        Status = NotificationStatus.NotSent;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (the order's buyer). Notifications are scoped to their order.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The E.164 destination. This is a shopper's number and is never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Null once disposed of (redacted). See <see cref="RedactContent"/>.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (e.g. an SM… SID). Null if the send was never accepted.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's current delivery outcome, mirrored verbatim. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    /// <summary>The provider error code when the message failed/was undelivered; otherwise null.</summary>
    public int? ErrorCode { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>When set, the message was queued with the provider to go out at this time (a scheduled follow-up).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is the product of a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Record the outcome of a create/schedule call to the provider.</summary>
    public void RecordSendResult(string? providerSid, string status, int? errorCode)
    {
        ProviderSid = providerSid;
        Status = status;
        ErrorCode = errorCode;
    }

    /// <summary>Advance the mirrored delivery status from a later provider fetch. Never regresses a terminal status.</summary>
    public void UpdateStatus(string status, int? errorCode)
    {
        if (NotificationStatus.IsTerminal(Status))
            return;
        Status = status;
        ErrorCode = errorCode;
    }

    public void MarkCanceled() => Status = NotificationStatus.Canceled;

    /// <summary>
    /// Dispose of the message content. The provider record and its delivery outcome survive; only the
    /// text is removed. Callers must also redact at the provider so the text is unretrievable there too.
    /// </summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void MarkResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public bool IsScheduled => ScheduledFor.HasValue;
}
