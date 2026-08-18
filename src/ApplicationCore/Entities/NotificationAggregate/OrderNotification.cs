using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// eShop's record of a single SMS raised for an order. It carries enough of the state the provider owns —
/// the message identifier (<see cref="ProviderMessageSid"/>) and its current delivery outcome
/// (<see cref="DeliveryStatus"/>) — that a later request can act on it (cancel a scheduled send, redact,
/// resend) and report on it, not only the request that first sent it.
///
/// It is its own aggregate root because operator endpoints act on a notification by id across orders
/// (resend, content disposal), and reconciliation ranges over all of them.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Provider delivery states that will never change again.
    private static readonly string[] TerminalStatuses =
        { "delivered", "undelivered", "failed", "canceled", "read" };

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        Kind = kind;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order — used to scope shopper-facing reads.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The E.164 destination. Sensitive: never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (Twilio SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, failed, undelivered, scheduled, canceled, ...).</summary>
    public string? DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When a follow-up is queued with the provider to be sent, set by the provider's schedule.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been disposed of (redacted) at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The message was accepted by the provider for immediate sending.</summary>
    public void RecordAccepted(string providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The message was accepted by the provider and scheduled for a future send.</summary>
    public void RecordScheduled(string providerMessageSid, string? status, DateTimeOffset sendAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        DeliveryStatus = status;
        ScheduledSendAt = sendAt;
        Touch();
    }

    /// <summary>
    /// The message could not be handed to the provider at all (e.g. a transport error). There is no SID and
    /// no provider status; we record a local failure so an operator can resend. The underlying order
    /// operation still succeeds.
    /// </summary>
    public void RecordSendFailure(string? errorMessage)
    {
        DeliveryStatus = "failed";
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
            DeliveryStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The scheduled send was called off before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = "canceled";
        Touch();
    }

    /// <summary>The message content has been disposed of; only the fact and outcome survive.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    /// <summary>True while the provider may still report a different outcome.</summary>
    public bool IsPending() =>
        ProviderMessageSid is not null &&
        (DeliveryStatus is null || Array.IndexOf(TerminalStatuses, DeliveryStatus) < 0);

    /// <summary>True if the message is scheduled at the provider and has not yet been sent.</summary>
    public bool IsScheduledPending() =>
        ProviderMessageSid is not null && string.Equals(DeliveryStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if this message did not reach the shopper and so is eligible for a resend.</summary>
    public bool IsUndelivered() =>
        string.Equals(DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DeliveryStatus, "undelivered", StringComparison.OrdinalIgnoreCase);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
