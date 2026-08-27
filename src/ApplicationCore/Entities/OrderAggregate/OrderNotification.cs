using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Records a single SMS sent (or attempted) for an order, together with the
/// provider-owned state (message identifier and delivery outcome) needed to act
/// on the message later: cancel it while scheduled, re-send it, redact its
/// content, or reconcile it against the provider's own records.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider rejected the send request outright.
    public const string LocalFailedStatus = "failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int? contactNumberId,
        NotificationType notificationType, string body,
        string? messageSid, string status,
        DateTimeOffset? scheduledForUtc = null, string? idempotencyKey = null,
        int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        NotificationType = notificationType;
        Body = body;
        MessageSid = messageSid;
        Status = status;
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = idempotencyKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationType NotificationType { get; private set; }
    public string? Body { get; private set; }
    public string? MessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledForUtc { get; private set; }
    public bool IsContentRedacted { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public bool IsScheduled => Status == "scheduled" && MessageSid is not null;

    public void UpdateStatus(string status, int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        IsContentRedacted = true;
    }
}
