using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS the shop sent (or tried to send) to a shopper about one of their
/// orders. It carries enough of the state the provider owns — the provider's message
/// identifier and the current delivery outcome — that a later request can act on the message
/// (re-send it, cancel it, redact it) and report on what became of it, not merely the request
/// that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
    #pragma warning restore CS8618

    private OrderNotification(int orderId, string buyerId, string toNumber, NotificationKind kind, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        Status = NotificationStatus.NotSent;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Factory for a message sent (or attempted) right away.</summary>
    public static OrderNotification CreateImmediate(int orderId, string buyerId, string toNumber, NotificationKind kind, string body)
        => new(orderId, buyerId, toNumber, kind, body);

    /// <summary>Factory for a follow-up scheduled with the provider to go out at a later time.</summary>
    public static OrderNotification CreateScheduled(int orderId, string buyerId, string toNumber, NotificationKind kind, string body, DateTimeOffset scheduledFor)
    {
        var n = new OrderNotification(orderId, buyerId, toNumber, kind, body)
        {
            IsScheduledFollowUp = true,
            ScheduledFor = scheduledFor
        };
        return n;
    }

    /// <summary>Factory for a notification produced by an operator re-send of an earlier one.</summary>
    public static OrderNotification CreateResend(OrderNotification original, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var n = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Kind, original.Body ?? string.Empty)
        {
            IdempotencyKey = idempotencyKey,
            OriginalNotificationId = original.Id
        };
        return n;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/message, used for shopper-scoped access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Destination number. Persisted for scoping and re-send; never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Cleared locally when a shopper asks for its content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID), once assigned.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last known delivery outcome. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the delivery-feedback follow-up that is queued with the provider for later.</summary>
    public bool IsScheduledFollowUp { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message body has been redacted at the provider and cleared locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Idempotency key supplied by the operator when this notification was produced by a re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one re-sent, if any.</summary>
    public int? OriginalNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the delivery outcome was last refreshed from the provider.</summary>
    public DateTimeOffset? StatusRefreshedAt { get; private set; }

    /// <summary>Records the provider's response after a send/schedule succeeded.</summary>
    public void RecordProviderResult(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        if (!string.IsNullOrEmpty(status))
        {
            Status = status!;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider send call failed before a message was ever created.</summary>
    public void MarkSendFailed(string? errorMessage, int? errorCode = null)
    {
        Status = NotificationStatus.SendFailed;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryOutcome(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status!;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks a scheduled message as called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        StatusRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Clears the local copy of the message text after the provider body has been redacted.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentRedacted = true;
    }
}
