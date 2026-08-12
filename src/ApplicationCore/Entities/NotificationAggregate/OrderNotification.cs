using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS message that went out (or was queued) for an order as it moved,
/// together with enough of the state the provider owns — its message identifier and current
/// delivery outcome — that a later request can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toNumber,
        string? body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        bool isScheduled,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        IsScheduled = isScheduled;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>The order this message concerns.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order — used to keep one shopper's data invisible to another.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (provider-canonical E.164). Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared locally when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (its message SID), when one was assigned.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last delivery outcome we know of from the provider (queued, sent, delivered, undelivered, failed, scheduled, canceled...).</summary>
    public string ProviderStatus { get; private set; }

    /// <summary>The provider's error code when the message failed or was undelivered.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>True for the follow-up that is queued with the provider to go out later.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted at the provider and cleared here).</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this record, when applicable.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this record is a resend, the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Records the latest provider identifier / delivery outcome for this message.</summary>
    public void UpdateProviderState(string? providerMessageSid, string providerStatus, int? providerErrorCode)
    {
        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }

        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }

        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>Marks a queued follow-up as called off before it went out.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
        IsScheduled = false;
    }

    /// <summary>Disposes of the message content locally (the provider-side redaction is done separately).</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
