using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop raised for an order. It carries enough of the state the
/// provider owns — the provider's message identifier and the current delivery outcome — that
/// a later request can act on the message (resend, cancel, redact) and report on it, rather
/// than only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local sentinel used before/without a provider message (no number on file, or a send that never reached the provider).</summary>
    public const string NotSentStatus = "not_sent";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string recipientOwnerId, NotificationKind kind, string body)
    {
        Guard.Against.NullOrEmpty(recipientOwnerId, nameof(recipientOwnerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        RecipientOwnerId = recipientOwnerId;
        Kind = kind;
        Body = body;
        DeliveryStatus = NotSentStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static OrderNotification ForImmediate(int orderId, string recipientOwnerId, NotificationKind kind, string body)
        => new(orderId, recipientOwnerId, kind, body);

    public static OrderNotification ForScheduled(int orderId, string recipientOwnerId, NotificationKind kind, string body, DateTimeOffset scheduledFor)
        => new(orderId, recipientOwnerId, kind, body) { IsScheduled = true, ScheduledFor = scheduledFor };

    public int OrderId { get; private set; }

    /// <summary>Identity (username) of the shopper the message is about — the order's owner.</summary>
    public string RecipientOwnerId { get; private set; }

    /// <summary>The contact number the message was addressed to, if any (null when there was none on file).</summary>
    public int? ContactNumberId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The text that was sent. Cleared once the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message, once it has accepted one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The current delivery outcome — the provider's status, or <see cref="NotSentStatus"/>.</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's numeric error code for a failed/undelivered message, if any.</summary>
    public int? ErrorCode { get; private set; }

    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message body has been redacted at the provider and cleared here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key of the resend that produced this record, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one is a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Record that the provider accepted a message and now owns its delivery.</summary>
    public void MarkAccepted(string providerMessageSid, string status, int? errorCode = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? DeliveryStatus : status;
        ErrorCode = errorCode;
    }

    /// <summary>Refresh the delivery outcome from the provider.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode)
    {
        if (!string.IsNullOrEmpty(status))
        {
            DeliveryStatus = status;
        }
        ErrorCode = errorCode;
    }

    public void AssignDestination(int contactNumberId) => ContactNumberId = contactNumberId;

    public void MarkIdempotency(string idempotencyKey) => IdempotencyKey = idempotencyKey;

    public void MarkResendOf(int originalNotificationId) => ResendOfNotificationId = originalNotificationId;

    /// <summary>Record that the message body has been disposed of. The record and its outcome survive.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}
