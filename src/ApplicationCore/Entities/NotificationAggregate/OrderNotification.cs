using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS this application produced for an order, together with the provider state it owns
/// (the message identifier and the last known delivery outcome) so a later request can act on it
/// and report on it — not only the request that sent it.
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
        string toPhoneNumber,
        string body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The order's buyer — used to scope shopper-facing reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The canonical E.164 destination. Sensitive — never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>
    /// The message text. Sensitive. Set to <c>null</c> once the content has been disposed of
    /// (see <see cref="MarkContentDisposed"/>), which also redacts it at the provider.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier (Twilio Message SID) for this message, once created.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last known provider delivery outcome (raw wire status, e.g. <c>delivered</c>).</summary>
    public string? Status { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider, if this is one.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a resend, the id of the original notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the outcome of the create call: the provider SID and initial status.</summary>
    public void RecordSendResult(string? providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Updates the last known delivery outcome (e.g. after re-reading it from the provider).</summary>
    public void UpdateStatus(string? status, int? errorCode = null, string? errorMessage = null)
    {
        if (status is not null)
        {
            Status = status;
        }
        if (errorCode is not null)
        {
            ErrorCode = errorCode;
        }
        if (errorMessage is not null)
        {
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>Marks a scheduled follow-up as called off before it went out.</summary>
    public void MarkCanceled() => Status = "canceled";

    /// <summary>
    /// Records that the content has been disposed of — cleared here and redacted at the provider.
    /// The fact that the message was sent, and what became of it, survives.
    /// </summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public bool IsScheduledFollowUp => Kind == NotificationKind.DeliveryFollowUp;
}
