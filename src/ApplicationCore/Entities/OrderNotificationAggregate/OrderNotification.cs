using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// eShop's record of a single SMS it tried to send about an order. It keeps enough of the state the
/// provider owns — the provider's message identifier (<see cref="MessageSid"/>) and the latest
/// delivery outcome (<see cref="Status"/>/<see cref="ErrorCode"/>) — that a later request can act on
/// and report about the message, not only the one that sent it. The destination
/// (<see cref="ToNumber"/>) is sensitive and is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber,
        bool isScheduledFollowUp = false, string? idempotencyKey = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        IsScheduledFollowUp = isScheduledFollowUp;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string OwnerId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The E.164 destination this message was addressed to (sensitive; never logged).</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's message identifier, once the provider accepted the message.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The latest known delivery outcome (a provider status, or <c>send_failed</c>).</summary>
    public string? Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True when this is the "how was delivery?" message queued with the provider for later.</summary>
    public bool IsScheduledFollowUp { get; private set; }

    /// <summary>True once the message's content has been disposed of at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Set on a notification produced by an operator resend, so repeats under the same key don't re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Records that the provider accepted the message and returned an identifier.</summary>
    public void RecordAccepted(string messageSid, string? status)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = status;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void RecordSendFailure()
    {
        Status = MessageDeliveryStatuses.SendFailed;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDelivery(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
            Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Records that a scheduled message was called off at the provider before it went out.</summary>
    public void MarkCanceledAtProvider()
    {
        Status = MessageDeliveryStatuses.Canceled;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
    }
}
