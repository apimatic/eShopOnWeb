using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS this application sent (or tried to send) about an order.
/// It deliberately carries the state the provider owns — its message identifier
/// (<see cref="ProviderMessageSid"/>) and its current delivery outcome
/// (<see cref="DeliveryStatus"/>) — so a later request can both act on the message
/// (resend, cancel a schedule, dispose of its content) and report on it, not merely the
/// request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>The order this notification is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The order's owning shopper (used to scope shopper-facing reads).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The destination number (canonical E.164). Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>
    /// The sending number as this application asked the provider to send it. Immediate
    /// notifications go from the configured Twilio:FromNumber; the scheduled follow-up goes
    /// through the Messaging Service. Reconciliation only lines up the FromNumber traffic.
    /// </summary>
    public string FromPhoneNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The provider's message SID once the provider has accepted the message; null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// The current delivery outcome. Holds the provider's own status wire value
    /// (queued, sending, sent, delivered, undelivered, failed, scheduled, canceled, ...) once
    /// the provider has accepted the message, or a local synthetic value (see
    /// <see cref="NotificationDeliveryStatus"/>) when the send never reached the provider.
    /// </summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The message text. Nulled out once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once a disposal request has removed the content here and at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>True for the dispatch follow-up, which is queued with the provider for the future.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend request that produced this record (if any).</summary>
    public string? ResendIdempotencyKey { get; private set; }

    /// <summary>The notification this record is a resend of (if any).</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        string toPhoneNumber,
        string fromPhoneNumber,
        NotificationKind kind,
        string body,
        bool isScheduled = false,
        DateTimeOffset? scheduledFor = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ToPhoneNumber = Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        FromPhoneNumber = Guard.Against.NullOrEmpty(fromPhoneNumber, nameof(fromPhoneNumber));
        Kind = kind;
        Body = Guard.Against.Null(body, nameof(body));
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        DeliveryStatus = NotificationDeliveryStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The provider accepted the message; record its identifier and initial status.</summary>
    public void MarkAccepted(string providerMessageSid, string deliveryStatus)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        DeliveryStatus = Guard.Against.NullOrEmpty(deliveryStatus, nameof(deliveryStatus));
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    /// <summary>The message could not be handed to the provider at all (network/API error). The order operation still succeeds.</summary>
    public void MarkSendFailed(string? providerErrorMessage, int? providerErrorCode = null)
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }

    /// <summary>Refresh the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryStatus(string deliveryStatus, int? providerErrorCode = null, string? providerErrorMessage = null)
    {
        if (!string.IsNullOrEmpty(deliveryStatus))
        {
            DeliveryStatus = deliveryStatus;
        }
        if (providerErrorCode.HasValue) ProviderErrorCode = providerErrorCode;
        if (!string.IsNullOrEmpty(providerErrorMessage)) ProviderErrorMessage = providerErrorMessage;
    }

    /// <summary>The scheduled message was called off at the provider before it went out.</summary>
    public void MarkScheduleCanceled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
    }

    /// <summary>The content has been disposed of here and at the provider; the send record survives.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Tag this record as the product of a resend under the given idempotency key.</summary>
    public void TagAsResend(int resendOfNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = resendOfNotificationId;
        ResendIdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
    }
}
