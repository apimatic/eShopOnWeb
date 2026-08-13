using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or tried to send) to a shopper about one of their
/// orders. It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and the current delivery outcome (<see cref="ProviderStatus"/>,
/// <see cref="ProviderErrorCode"/>) — that a later request can act on it (resend, cancel a scheduled
/// follow-up, dispose of its content) and report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local sentinel used when the provider never accepted the message (create call failed).</summary>
    public const string NotSentStatus = "not_sent";

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about / addressed to. Scopes every read to its owner.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>
    /// The registered contact number this message was addressed to. Kept so a resend can re-resolve
    /// the destination and so that a resend is refused once the shopper has removed that number.
    /// </summary>
    public int ContactNumberId { get; private set; }

    /// <summary>The provider's message SID (e.g. <c>SM...</c>), once the provider accepted the create.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current status verbatim (queued, sent, delivered, undelivered, failed, scheduled, canceled…) or a local sentinel.</summary>
    public string ProviderStatus { get; private set; } = NotSentStatus;

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>
    /// The text that was sent. Cleared (and redacted at the provider) when a shopper asks for the
    /// content to be disposed of; the fact of the message and its outcome survive.
    /// </summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>True for the delivery follow-up that is queued with the provider to send days later.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the provider reports it actually sent the message (its <c>date_sent</c>).</summary>
    public DateTimeOffset? ProviderSentAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, so a repeat under the same key sends nothing new.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is the product of a resend, the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, int contactNumberId, string body,
        bool isScheduled = false, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>The provider accepted the create call. Records the SID and the state it reported.</summary>
    public void RecordAccepted(string sid, string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ProviderStatus = string.IsNullOrEmpty(status) ? ProviderStatus : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderSentAt = sentAt;
    }

    /// <summary>The create call never produced a message (e.g. transport error). Recorded so an operator can resend.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        ProviderStatus = NotSentStatus;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh from a later fetch of the provider's message resource.</summary>
    public void UpdateProviderState(string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (sentAt.HasValue)
        {
            ProviderSentAt = sentAt;
        }
    }

    /// <summary>Locally reflect that a scheduled message was cancelled at the provider.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
    }

    /// <summary>Clear the stored text after it has been redacted at the provider.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Whether the delivery outcome is final and no longer worth polling the provider for.</summary>
    public bool IsTerminal =>
        ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";

    /// <summary>Whether this message reached the shopper (used to gate an operator resend).</summary>
    public bool ReachedRecipient =>
        ProviderStatus is "delivered" or "received" or "read";
}
