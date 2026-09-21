using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS raised about an order for a shopper. It carries enough of the state the provider owns
/// — the provider's message identifier and the current delivery outcome — that a later request can act
/// on it (resend, dispose content) and report on it (my-orders, notifications), not only the request
/// that sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(string buyerId, int orderId, NotificationKind kind, string toNumber, string body,
        bool isScheduled, DateTimeOffset? scheduledFor, DateTimeOffset createdAt, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        CreatedAt = createdAt;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Owner — the shopper the message is about.</summary>
    public string BuyerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Emptied once its content has been disposed of at the provider.</summary>
    public string Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True for the delivery follow-up, which is queued with the provider to go out later.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    // --- Provider-owned state ---

    /// <summary>The provider's identifier for this message (Twilio message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (raw provider status, e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Set when the send itself could not be performed (provider unreachable or the API rejected the request).
    /// Distinct from a message the provider accepted and the carrier later refused (that shows up as a delivery status).</summary>
    public string? SendError { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification, if it came from a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The provider accepted the message; record its identifier and initial status.</summary>
    public void RecordAccepted(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        SendError = null;
    }

    /// <summary>The message could not be handed to the provider at all.</summary>
    public void RecordSendError(string reason)
    {
        SendError = reason;
    }

    /// <summary>Refresh the delivery outcome from the provider's own record.</summary>
    public void UpdateDeliveryStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (providerStatus is not null)
            ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>The scheduled follow-up was called off with the provider before it went out.</summary>
    public void MarkCancelledWithProvider()
    {
        ProviderStatus = "canceled";
    }

    /// <summary>The message content has been disposed of at the provider; drop the local copy too.</summary>
    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = string.Empty;
    }
}
