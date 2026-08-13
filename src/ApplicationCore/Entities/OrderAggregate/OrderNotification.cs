using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A record of a single SMS the shop sent (or tried to send) to a shopper about one of their
/// orders. It carries enough of the provider-owned state — the provider's message identifier
/// and the current delivery outcome — that a later request can act on it (resend, cancel a
/// scheduled follow-up, dispose of its content) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string messageBody)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        MessageBody = messageBody;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (order's buyer). Used to scope shopper-facing reads.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination number. Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>
    /// Local copy of the message text. Set to null once the content has been disposed of at the
    /// shopper's request (the copy at the provider is redacted separately).
    /// </summary>
    public string? MessageBody { get; private set; }

    /// <summary>The provider's identifier for the message, once it has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome — the provider's status, or a local outcome when the send never reached the provider.</summary>
    public string Status { get; private set; }

    /// <summary>True for the "how did delivery go?" follow-up queued with the provider for later.</summary>
    public bool IsScheduled { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this notification was produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one is a resend of, when applicable.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>Record that the provider accepted the message (immediate or scheduled).</summary>
    public void MarkQueued(string providerMessageSid, string providerStatus, bool isScheduled = false)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(providerStatus) ? NotificationStatus.Queued : providerStatus;
        IsScheduled = isScheduled;
    }

    /// <summary>Record that the send could not be handed to the provider at all. Never fails the caller's operation.</summary>
    public void MarkSendFailed()
    {
        Status = NotificationStatus.SendFailed;
    }

    /// <summary>Refresh the delivery outcome from the provider.</summary>
    public void UpdateStatus(string providerStatus)
    {
        if (!string.IsNullOrEmpty(providerStatus))
            Status = providerStatus;
    }

    public void SetProviderDateSent(DateTimeOffset? dateSent)
    {
        if (dateSent.HasValue)
            ProviderDateSent = dateSent;
    }

    /// <summary>Mark the scheduled follow-up as called off before it went out.</summary>
    public void MarkCancelled()
    {
        Status = NotificationStatus.Canceled;
    }

    /// <summary>Dispose of the local content. The provider-side redaction is performed separately.</summary>
    public void RedactContent()
    {
        MessageBody = null;
        ContentRedacted = true;
    }

    public void MarkAsResendOf(int sourceNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Whether the delivery outcome is settled and no longer worth re-querying the provider for.</summary>
    public bool IsTerminal() => NotificationStatus.IsTerminal(Status);
}
