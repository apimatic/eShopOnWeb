using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or scheduled) to a shopper about an order.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and the current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on the message (cancel, resend, redact) and report on it,
/// not only the request that first sent it.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    // Provider (Twilio) message status strings we care about. A message that reached the shopper
    // is "delivered"; a message that did not is "undelivered" or "failed"; a scheduled follow-up
    // sits in "scheduled" until it sends or is called off ("canceled").
    public const string StatusScheduled = "scheduled";
    public const string StatusCanceled = "canceled";
    public const string StatusDelivered = "delivered";
    public const string StatusUndelivered = "undelivered";
    public const string StatusFailed = "failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
#pragma warning restore CS8618

    private SmsNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        MessageBody = body;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    /// <summary>Creates a notification that is sent immediately.</summary>
    public static SmsNotification ForImmediateSend(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
        => new(orderId, buyerId, kind, toNumber, body);

    public int OrderId { get; private set; }

    /// <summary>Owning shopper (the username carried on the JWT). Scopes who may see the notification.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in E.164. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Local copy of the message text. Cleared when content disposal is requested.</summary>
    public string MessageBody { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string Status { get; private set; }

    /// <summary>Provider error code when a send/schedule could not be accepted, or delivery failed.</summary>
    public int? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>For a scheduled follow-up, when the provider will send it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message text has been disposed of (locally and at the provider).</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator re-send; null for messages that were not produced by a re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Records the outcome of handing the message to the provider.</summary>
    public void SetProviderResult(string? providerMessageSid, string status, int? errorCode = null, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        if (scheduledFor.HasValue) ScheduledFor = scheduledFor;
    }

    /// <summary>Records that the send could not be handed to the provider at all.</summary>
    public void MarkSendFailed(int? errorCode = null)
    {
        Status = StatusFailed;
        ErrorCode = errorCode;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view.</summary>
    public void UpdateStatus(string status, int? errorCode)
    {
        Status = status;
        if (errorCode.HasValue) ErrorCode = errorCode;
    }

    /// <summary>Marks a scheduled follow-up as called off with the provider.</summary>
    public void MarkCanceled()
    {
        Status = StatusCanceled;
    }

    /// <summary>Disposes of the message text. The record that a message was sent, and its outcome, survive.</summary>
    public void RedactContent()
    {
        MessageBody = string.Empty;
        ContentRedacted = true;
    }

    public void AssignIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>A follow-up still queued with the provider that has not yet gone out.</summary>
    public bool IsScheduledAndPending => Kind == NotificationKind.DeliveryFollowUp
        && !string.IsNullOrEmpty(ProviderMessageSid)
        && string.Equals(Status, StatusScheduled, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the provider's latest outcome says the message did not reach the shopper.</summary>
    public bool DidNotReachShopper => string.Equals(Status, StatusUndelivered, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, StatusFailed, StringComparison.OrdinalIgnoreCase);
}
