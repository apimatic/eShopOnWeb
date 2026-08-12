using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single order-progress message about a shopper. It carries enough of the state the
/// provider owns — the provider's message identifier and the current delivery outcome — that a
/// later request can act on the message (resend, cancel, dispose its content) and report on it,
/// not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string ownerId, string toNumber, string body, NotificationKind kind,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        ToNumber = toNumber;
        Body = body;
        Kind = kind;
        IdempotencyKey = idempotencyKey;
        Status = NotificationDeliveryStatus.PendingSend;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the message is about (their user name).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Destination number in E.164 form. A shopper contact detail: never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The message text. Null once the content has been disposed of at the shopper's request; the
    /// record that a message was sent, and what became of it, survives disposal.
    /// </summary>
    public string? Body { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The provider's identifier for the message, once it has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome, kept in step with the provider.</summary>
    public NotificationDeliveryStatus Status { get; private set; }

    /// <summary>Provider error code for a message that could not be delivered, when one is available.</summary>
    public int? ProviderErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>For a scheduled follow-up: when the provider is due to send it.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key, present on a notification produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message content has been disposed of on the provider's side.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>
    /// True while the message is a follow-up the provider is holding to send in the future and
    /// which has not yet been sent — i.e. one that can still be called off.
    /// </summary>
    public bool IsCancellableFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp &&
        Status == NotificationDeliveryStatus.Scheduled &&
        ProviderMessageSid is not null;

    private static readonly NotificationDeliveryStatus[] _terminalStatuses =
    {
        NotificationDeliveryStatus.Delivered,
        NotificationDeliveryStatus.Undelivered,
        NotificationDeliveryStatus.Failed,
        NotificationDeliveryStatus.Canceled,
        NotificationDeliveryStatus.Read,
        NotificationDeliveryStatus.PartiallyDelivered
    };

    /// <summary>
    /// True when the delivery outcome is settled and no longer worth refreshing from the provider.
    /// </summary>
    public bool IsTerminal => Array.IndexOf(_terminalStatuses, Status) >= 0;

    /// <summary>Records that the provider accepted the message for immediate sending.</summary>
    public void MarkSent(string providerMessageSid, NotificationDeliveryStatus status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ProviderErrorCode = errorCode;
    }

    /// <summary>Records that the provider accepted the message and scheduled it for later delivery.</summary>
    public void MarkScheduled(string providerMessageSid, DateTimeOffset sendAt, NotificationDeliveryStatus status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ScheduledSendAt = sendAt;
        Status = status;
    }

    /// <summary>
    /// Records that the message could not be handed to the provider at all. The underlying order
    /// operation still succeeds; the failure is captured here so an operator can act on it.
    /// </summary>
    public void MarkSendFailed(int? errorCode)
    {
        Status = NotificationDeliveryStatus.Failed;
        ProviderErrorCode = errorCode;
    }

    /// <summary>Brings the stored delivery outcome in step with the provider's latest view.</summary>
    public void UpdateDeliveryState(NotificationDeliveryStatus status, int? errorCode)
    {
        Status = status;
        if (errorCode.HasValue)
        {
            ProviderErrorCode = errorCode;
        }
    }

    /// <summary>Records that a scheduled follow-up was called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationDeliveryStatus.Canceled;
    }

    /// <summary>
    /// Disposes of the message content locally. Call only once the content has also been disposed of
    /// on the provider's side; the record and its delivery outcome survive.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
