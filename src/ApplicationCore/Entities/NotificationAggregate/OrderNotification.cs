using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message about an order and what became of it at the provider. The row is created
/// <em>before</em> the provider is called (carrying its destination and, for a resend, the caller's
/// idempotency key) and completed with the provider's identifier and status afterwards — so a message
/// the provider has always has a local row to reconcile against.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toNumber,
        string? fromAddress,
        string? body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        FromAddress = fromAddress;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        DeliveryState = NotificationDeliveryState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>Owning shopper (login name / email), denormalised so notifications scope like orders.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination E.164 number. Sensitive: never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The sender used. The configured <c>Twilio:FromNumber</c> for immediate messages (so
    /// reconciliation's From filter finds them); null for a scheduled message sent via the messaging
    /// service (whose From is only chosen when it actually sends).
    /// </summary>
    public string? FromAddress { get; private set; }

    /// <summary>Message text. Cleared locally when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider identifier (message SID). Null until the provider accepts the message.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's own raw status string (e.g. <c>delivered</c>, <c>undelivered</c>).</summary>
    public string? ProviderStatus { get; private set; }

    public NotificationDeliveryState DeliveryState { get; private set; }

    /// <summary>Provider error code when the message failed/undelivered; null otherwise.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>Provider send time (raw string), when known.</summary>
    public string? ProviderDateSent { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public bool ScheduledCanceled { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend (unique). Null for non-resend messages.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is itself a resend, the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Record the provider's acceptance of a (possibly scheduled) message.</summary>
    public void RecordAccepted(string messageSid, string? providerStatus, NotificationDeliveryState state,
        int? errorCode, string? dateSent)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        ProviderStatus = providerStatus;
        DeliveryState = state;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        Touch();
    }

    /// <summary>Record that the provider call failed before any Sid was obtained.</summary>
    public void RecordSendFailed()
    {
        DeliveryState = NotificationDeliveryState.SendFailed;
        Touch();
    }

    /// <summary>Refresh from a later provider read.</summary>
    public void RefreshFromProvider(string? providerStatus, NotificationDeliveryState state, int? errorCode,
        string? dateSent)
    {
        ProviderStatus = providerStatus;
        DeliveryState = state;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        Touch();
    }

    public void MarkScheduledCanceled()
    {
        ScheduledCanceled = true;
        DeliveryState = NotificationDeliveryState.Canceled;
        ProviderStatus = "canceled";
        Touch();
    }

    /// <summary>Dispose of the message content locally (the provider redaction is done separately).</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    /// <summary>A message that did not reach the shopper and so may legitimately be re-sent.</summary>
    public bool DidNotReach() =>
        DeliveryState is NotificationDeliveryState.Failed
            or NotificationDeliveryState.Undelivered
            or NotificationDeliveryState.SendFailed;

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
