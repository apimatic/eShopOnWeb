using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message that eShop sent (or attempted to send) to a shopper about one of their orders.
/// It records enough of the state the provider owns — the provider's message identifier and the
/// current delivery outcome — that a later request can act on it (resend, cancel a scheduled
/// follow-up, dispose of its content) and report on it, not only the request that created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about (their user name / login).</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>
    /// The canonical destination number. Held so a resend can reach the same shopper and so a send to a
    /// number that has since been removed can be refused. Never written to logs.
    /// </summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The message text. Nullable because it is cleared when a shopper asks for the content to be
    /// disposed of (the record and delivery outcome survive).
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID), once the provider has accepted the message.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its wire status value), e.g. queued / sent / delivered / undelivered / failed / scheduled / canceled.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>
    /// True when the message could not be handed to the provider at all (e.g. the provider was
    /// unreachable). The underlying order operation still succeeded; this simply records that no
    /// message left. Distinct from a message the provider accepted and later reported undelivered/failed.
    /// </summary>
    public bool SendFailed { get; private set; }

    /// <summary>A caller-safe reason a send failed. Never contains the destination number.</summary>
    public string? SendFailureReason { get; private set; }

    /// <summary>When set, the message was scheduled with the provider to be sent at this time.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the content has been disposed of at the provider and cleared here.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The idempotency key of the resend request that produced this notification, if any.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message and returned its identifier and initial status.</summary>
    public void RecordAccepted(string providerSid, string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
        SendFailed = false;
        SendFailureReason = null;
    }

    /// <summary>Records that the message could not be handed to the provider. The order operation still succeeds.</summary>
    public void RecordSendFailure(string reason)
    {
        SendFailed = true;
        SendFailureReason = reason;
        ProviderSid = null;
        ProviderStatus = null;
    }

    /// <summary>Updates the last-known delivery outcome from a fresh reading of provider state.</summary>
    public void UpdateDeliveryState(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Marks a (previously scheduled) message as canceled after it was called off at the provider.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
    }

    /// <summary>Clears the stored content after it has been disposed of at the provider. The record survives.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void AssignResendKey(string idempotencyKey)
    {
        ResendIdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// A scheduled follow-up that has not yet gone out — the only kind that can still be called off.
    /// </summary>
    public bool IsPendingScheduled =>
        ScheduledSendAt.HasValue && !SendFailed &&
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
}
