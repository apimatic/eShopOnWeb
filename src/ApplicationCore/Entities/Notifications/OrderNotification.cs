using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// A single SMS the shop attempted to send to a shopper about an order. It carries the
/// provider's own state — the message identifier and the latest delivery outcome — so a
/// later request can act on it (resend / redact / reconcile) and report on it, not merely
/// the request that first created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local state before the provider has been asked to send anything.</summary>
    public const string StatusPending = "pending";

    /// <summary>Local state: the provider could not be reached / rejected the send outright.</summary>
    public const string StatusSendFailed = "send_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toPhoneNumber,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ToPhoneNumber = Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        Kind = kind;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in canonical E.164. Persisted, but never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The app-composed message text. Nulled out once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio message SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The latest delivery outcome. Either a provider status or a local sentinel.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When set, the message is queued with the provider to go out at this time.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is the product of a re-send, the id of the original.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records that the provider accepted the send and returned an identifier.</summary>
    public void RecordProviderAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Status = string.IsNullOrEmpty(status) ? Status : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = StatusSendFailed;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Applies the provider's latest view of the delivery outcome.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status))
            return;

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Disposes of the message content locally after it has been redacted at the provider.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
