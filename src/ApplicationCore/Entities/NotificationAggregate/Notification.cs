using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS eShop raised about an order. It carries enough of the state the provider
/// owns — its message identifier (<see cref="ProviderMessageSid"/>) and current delivery outcome
/// (<see cref="ProviderStatus"/>) — that a later request can act on it (resend, cancel a scheduled
/// message, dispose its content) and report on it, not only the request that first sent it.
///
/// The message <see cref="Body"/> can be disposed at the shopper's request; when that happens the
/// text is cleared here and redacted at the provider, but the fact a message was sent and what
/// became of it survives.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(string buyerId, int orderId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ProviderStatus = SmsDeliveryStatus.Pending;
    }

    public string BuyerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (E.164). Persisted but never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed.</summary>
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last-known delivery outcome as reported by the provider (or a local marker before/without a send).</summary>
    public string ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When this message is scheduled to be sent by the provider (for a <see cref="NotificationKind.DeliveryFollowUp"/>).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Provider-reported send time, used when reconciling against the provider's records.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator resend; null for messages raised by the order flow.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one re-sent, when <see cref="Kind"/> is <see cref="NotificationKind.Resend"/>.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Records the outcome of a successful create/schedule call to the provider.</summary>
    public void RecordProviderResult(string providerMessageSid, string providerStatus, DateTimeOffset? dateSent = null,
        int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = string.IsNullOrEmpty(providerStatus) ? SmsDeliveryStatus.Unknown : providerStatus;
        ProviderDateSent = dateSent ?? ProviderDateSent;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Records that the send never reached the provider. The order operation still succeeds.</summary>
    public void RecordSendFailure(string errorMessage)
    {
        ProviderStatus = SmsDeliveryStatus.SendFailed;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryStatus(string providerStatus, DateTimeOffset? dateSent = null,
        int? errorCode = null, string? errorMessage = null)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
        if (errorCode.HasValue)
        {
            ProviderErrorCode = errorCode;
        }
        if (!string.IsNullOrEmpty(errorMessage))
        {
            ProviderErrorMessage = errorMessage;
        }
    }

    public void MarkScheduled(DateTimeOffset scheduledFor)
    {
        ScheduledFor = scheduledFor;
    }

    public void MarkCancelled()
    {
        ProviderStatus = SmsDeliveryStatus.Canceled;
    }

    /// <summary>Clears the stored text after the provider copy has been redacted.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }

    public void SetResendOf(int originalNotificationId)
    {
        ResendOfNotificationId = originalNotificationId;
    }
}
