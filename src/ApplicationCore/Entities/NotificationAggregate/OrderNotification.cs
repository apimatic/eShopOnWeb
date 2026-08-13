using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop attempted to send a shopper about one of their orders.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery <see cref="Status"/> — that a later
/// request can act on it (cancel, resend, redact) and report on it, not only the request that
/// created it. It belongs to the shopper the order belongs to (<see cref="OwnerId"/>).
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toPhoneNumber, string body)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Kind = kind;
        ToPhoneNumber = Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        Status = DeliveryStatus.NotSent;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the order (and so this notification) belongs to.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number, in E.164. PII — never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The text the shop sent. Cleared when the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (e.g. <c>SM…</c>). Null if it was never created.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The current delivery outcome, as owned by the provider. See <see cref="DeliveryStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for a message queued with the provider to go out later (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and cleared locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend request that produced this record, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this record was produced by an operator re-send, the id of the message it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Record the outcome of creating the message at the provider (send or schedule).</summary>
    public void RecordProviderResult(string? sid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        IsScheduled = true;
        ScheduledSendAt = sendAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery state from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark that the message text has been disposed of at the provider and locally.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsResendOf(int originalNotificationId, string? idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsTerminal() => DeliveryStatus.IsTerminal(Status);
    public bool ReachedRecipient() => DeliveryStatus.ReachedRecipient(Status);
    public bool DidNotReachRecipient() => DeliveryStatus.DidNotReachRecipient(Status);
}
