using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message raised for an order. Carries enough of the state the provider owns — its
/// message identifier (<see cref="ProviderMessageSid"/>) and current delivery outcome
/// (<see cref="DeliveryStatus"/>) — that a later request can act on it and report on it, not only
/// the one that sent it. Belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(string buyerId, int orderId, NotificationType type, string toNumber, string body)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        OrderId = orderId;
        Type = type;
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        DeliveryStatus = NotificationDeliveryStatus.Pending;
    }

    /// <summary>Owning shopper (the recipient's identity).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order this message relates to.</summary>
    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Provider's canonical destination (E.164). Stored for reconciliation; never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once the content has been disposed at the shopper's request.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>Provider message identifier; null if the provider never accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome — a provider wire status once sent, else a local marker.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When a scheduled (follow-up) message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Records the outcome of an immediate send.</summary>
    public void RecordSent(string? providerSid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerSid;
        DeliveryStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Records the outcome of a message the provider is holding to send later.</summary>
    public void RecordScheduled(string? providerSid, string status, DateTimeOffset sendAt)
    {
        ProviderMessageSid = providerSid;
        DeliveryStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        ScheduledSendAt = sendAt;
    }

    /// <summary>
    /// Records that the message could not be sent. The reason must never contain the contact number.
    /// </summary>
    public void RecordSendFailure(string reason)
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
        ProviderErrorMessage = reason;
    }

    /// <summary>Updates the delivery outcome from a later fetch/cancel of the provider's record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        DeliveryStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void AssignIdempotencyKey(string key)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(key, nameof(key));
    }

    /// <summary>
    /// Disposes of the message content locally. The fact a message was sent and what became of it
    /// (identifier, status) survives; only the text is removed.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
