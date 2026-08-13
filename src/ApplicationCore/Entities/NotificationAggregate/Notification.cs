using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS this application raised for an order. It carries enough of the
/// state the provider owns — the provider's message identifier and the current delivery
/// outcome — that a later request can act on the message (cancel, resend, redact) and report
/// on it, not merely the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    private Notification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        Kind = kind;
        CreatedDate = DateTimeOffset.UtcNow;
        ProviderStatus = MessageDeliveryStatus.NotSent;
    }

    /// <summary>Creates a notification for an immediately-sent message.</summary>
    public static Notification ForImmediate(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
        => new(orderId, ownerId, kind, toNumber, body);

    /// <summary>Creates a notification for a message scheduled with the provider for future delivery.</summary>
    public static Notification ForScheduled(int orderId, string ownerId, NotificationKind kind, string toNumber, string body, DateTimeOffset sendAt)
        => new(orderId, ownerId, kind, toNumber, body) { ScheduledSendAt = sendAt };

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about (the order's buyer). Used to scope shopper access.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in canonical E.164. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (Twilio SID), once the provider has accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last known provider delivery status. See <see cref="MessageDeliveryStatus"/>.</summary>
    public string ProviderStatus { get; private set; }

    public string? ProviderErrorCode { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and dropped locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>
    /// The caller-supplied idempotency key that produced this notification, when it was created by a
    /// re-send. Lets a repeated re-send under the same key return this same message instead of sending again.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Records the provider's response to accepting the message for (scheduled or immediate) delivery.</summary>
    public void RecordProviderAccepted(string providerMessageSid, string providerStatus, string? errorCode = null)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderStatus = Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));
        ProviderErrorCode = errorCode;
    }

    /// <summary>Records that the provider could not be asked to send (the message never left this app).</summary>
    public void RecordSendFailed(string? errorCode = null)
    {
        ProviderStatus = MessageDeliveryStatus.NotSent;
        ProviderErrorCode = errorCode;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryStatus(string providerStatus, string? errorCode = null)
    {
        if (string.IsNullOrEmpty(providerStatus)) return;
        ProviderStatus = providerStatus;
        if (errorCode != null) ProviderErrorCode = errorCode;
    }

    public void MarkCanceled() => ProviderStatus = MessageDeliveryStatus.Canceled;

    public void SetIdempotencyKey(string key) => IdempotencyKey = Guard.Against.NullOrEmpty(key, nameof(key));

    /// <summary>
    /// Drops the local copy of the message text after it has been redacted at the provider. The record
    /// of the message having been sent, and of what became of it, survives.
    /// </summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>Whether this message is the delivery follow-up still awaiting its scheduled send.</summary>
    public bool IsPendingFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp &&
        ProviderStatus is MessageDeliveryStatus.Scheduled &&
        ProviderMessageSid != null;
}
