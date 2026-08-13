using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message eShop sent (or tried to send) to a shopper about one of their orders.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderSid"/>) and its current delivery outcome (<see cref="ProviderStatus"/> /
/// <see cref="Status"/>) — that a later request can act on it (resend, cancel, dispose) and
/// report on it, not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(
        int orderId,
        string buyerId,
        string toNumber,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper this message is for (the owner, for scoping).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The destination number (canonical E.164). Stored, but never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>eShop's normalized status. Terminal values never get refreshed again.</summary>
    public NotificationStatus Status { get; private set; }

    /// <summary>The provider's own raw status string (e.g. "delivered", "undelivered").</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The provider's message identifier (Twilio message SID). Null if never created.</summary>
    public string? ProviderSid { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The message text. Cleared once a shopper asks for it to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once the content has been disposed of (here and at the provider).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key, set on messages produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When set, the notification this one was a resend of.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>For a follow-up, the time it is queued to go out at the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the provider accepted the create call (used to scope reconciliation).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>
    /// Records a successful hand-off to the provider. <paramref name="sentAt"/> is null for a
    /// scheduled message that has not gone out yet, so it does not count as sent for reconciliation.
    /// </summary>
    public void RecordAccepted(string providerSid, string providerStatus, DateTimeOffset? sentAt)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));

        ProviderSid = providerSid;
        SentAt = sentAt;
        ApplyProviderStatus(providerStatus, errorCode: null, errorMessage: null);
    }

    /// <summary>Records that the provider rejected the create call outright (no message exists).</summary>
    public void RecordSendFailure(int? errorCode, string? errorMessage)
    {
        Status = NotificationStatus.SendFailed;
        ProviderStatus = null;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Applies the latest status the provider reports for this message.</summary>
    public void ApplyProviderStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Status = MapStatus(providerStatus);
    }

    /// <summary>Marks a scheduled message as called off.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        ProviderStatus = "canceled";
    }

    /// <summary>Disposes of the message content locally (the provider copy is redacted separately).</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>True once nothing more will change for this message, so it need not be refreshed.</summary>
    public bool IsTerminal =>
        Status is NotificationStatus.Delivered
            or NotificationStatus.Undelivered
            or NotificationStatus.Failed
            or NotificationStatus.Canceled
            or NotificationStatus.SendFailed;

    /// <summary>True when the message did not reach the shopper, so a resend is warranted.</summary>
    public bool DidNotReachRecipient =>
        Status is NotificationStatus.Undelivered
            or NotificationStatus.Failed
            or NotificationStatus.SendFailed;

    private static NotificationStatus MapStatus(string providerStatus) =>
        providerStatus?.ToLowerInvariant() switch
        {
            "delivered" => NotificationStatus.Delivered,
            "undelivered" => NotificationStatus.Undelivered,
            "failed" => NotificationStatus.Failed,
            "canceled" => NotificationStatus.Canceled,
            "cancelled" => NotificationStatus.Canceled,
            "scheduled" => NotificationStatus.Scheduled,
            "accepted" => NotificationStatus.Sending,
            "queued" => NotificationStatus.Sending,
            "sending" => NotificationStatus.Sending,
            "sent" => NotificationStatus.Sending,
            "receiving" => NotificationStatus.Sending,
            "received" => NotificationStatus.Sending,
            _ => NotificationStatus.Sending
        };
}
