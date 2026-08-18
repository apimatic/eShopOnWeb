using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or tried to send) to a shopper about one of
/// their orders. It carries enough of the state the provider owns — the provider's message
/// identifier and the last delivery outcome it reported — that a later request can act on the
/// message (resend it, cancel a scheduled one, dispose of its content) and report on it,
/// without having to have been the request that originally sent it.
///
/// The destination number and the message body are sensitive and are never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local marker used when the provider never accepted the message at all
    /// (e.g. a transport error before a message id was ever issued).</summary>
    public const string SubmissionFailedStatus = "submission_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, string toPhoneNumber, NotificationKind kind, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToPhoneNumber = toPhoneNumber;
        Kind = kind;
        Body = body;
        ProviderStatus = SubmissionFailedStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper this message is about (their buyer id / user name).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The destination number in E.164. Sensitive — never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Sensitive — never logged. Null once its content has been
    /// disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message, once it accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's most recently observed delivery outcome for this message
    /// (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled), or the local
    /// <see cref="SubmissionFailedStatus"/> marker if the provider never accepted it.</summary>
    public string ProviderStatus { get; private set; }

    /// <summary>The provider's numeric error code when the message failed or was undelivered.</summary>
    public int? ProviderErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the provider accepted the message for (immediate or scheduled) sending.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>For a scheduled message, when the provider is due to send it.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public DateTimeOffset? StatusRefreshedAt { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted at the provider
    /// and cleared here). The fact a message was sent, and what became of it, survives.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this message,
    /// if it was produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one is a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True for the scheduled delivery follow-up while it is still pending — i.e. a
    /// candidate to be called off if the order is cancelled.</summary>
    public bool IsPendingScheduledFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp &&
        ProviderMessageSid is not null &&
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    /// <summary>Records that the provider accepted the message and issued an id for it.</summary>
    public void RecordSubmitted(string providerMessageSid, string providerStatus, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ScheduledSendAt = scheduledSendAt;
        SubmittedAt = DateTimeOffset.UtcNow;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider never accepted the message.</summary>
    public void RecordSubmissionFailed()
    {
        ProviderStatus = SubmissionFailedStatus;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Updates the last-known delivery outcome from a fresh read of the provider.</summary>
    public void UpdateDeliveryState(string providerStatus, int? providerErrorCode)
    {
        if (string.IsNullOrEmpty(providerStatus))
        {
            return;
        }

        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that a scheduled message was called off before it went out.</summary>
    public void MarkScheduleCanceled()
    {
        ProviderStatus = "canceled";
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Clears the message content locally after it has been redacted at the provider.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Creates a fresh notification that re-sends the message this one carried.</summary>
    public OrderNotification CreateResend(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(Body, nameof(Body),
            "Cannot resend a message whose content has been disposed of.");

        return new OrderNotification(OrderId, BuyerId, ToPhoneNumber, Kind, Body!)
        {
            ResendOfNotificationId = Id,
            IdempotencyKey = idempotencyKey
        };
    }
}
