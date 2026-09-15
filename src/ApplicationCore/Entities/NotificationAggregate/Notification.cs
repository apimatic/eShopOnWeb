using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single text message sent (or attempted) to a shopper about one of their orders.
/// It carries enough of the state the provider owns — the provider's message identifier and the
/// current delivery outcome — that a later request can act on it (resend, dispose, reconcile) and
/// report on it, not merely the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(
        int orderId,
        string ownerId,
        string toNumber,
        NotificationType type,
        string body,
        bool isScheduledFollowUp = false,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        IsScheduledFollowUp = isScheduledFollowUp;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity (user name) of the shopper the message concerns. Used to scope every read.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The canonical destination number. Treated as PII; never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once a disposal request has removed the content (here and at the provider).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's own identifier for this message (its message SID), once created.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The current delivery outcome, carried from the provider. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }

    /// <summary>True for the "how did delivery go?" message queued with the provider for a later date.</summary>
    public bool IsScheduledFollowUp { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key, when this message was produced by an operator resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Records the result of a successful create/schedule call to the provider.</summary>
    public void RecordAccepted(string providerMessageId, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        ProviderMessageId = providerMessageId;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider could not be asked to send (the send never left this app).</summary>
    public void RecordSendFailure()
    {
        Status = NotificationStatus.SendFailed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Updates the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryState(string status, int? errorCode)
    {
        if (string.IsNullOrEmpty(status)) return;
        Status = status;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks a not-yet-sent scheduled message as called off.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Disposes of the message content locally. The fact that a message was sent, and what became
    /// of it, survives; only the text is removed.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
