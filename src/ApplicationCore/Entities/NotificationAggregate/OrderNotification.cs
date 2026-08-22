using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string LocalSendFailedStatus = "send_failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        int? contactNumberId,
        string? destinationE164,
        string? body,
        DateTimeOffset? scheduledFor = null,
        int? parentNotificationId = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        DestinationE164 = destinationE164;
        Body = body;
        ScheduledFor = scheduledFor;
        ParentNotificationId = parentNotificationId;
        Status = "created";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? DestinationE164 { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ParentNotificationId { get; private set; }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public void ApplyProviderResult(
        string? providerSid,
        string? status,
        string? body,
        int? errorCode,
        string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            ProviderSid = providerSid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }

        if (!ContentDisposed && body is not null)
        {
            Body = body;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkLocalSendFailed(string errorMessage)
    {
        Status = LocalSendFailedStatus;
        ErrorMessage = errorMessage;
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }

    public bool DidNotReachShopper()
    {
        var status = Status?.Trim().ToLowerInvariant();
        return status is "failed" or "undelivered" or "canceled" or "cancelled" or LocalSendFailedStatus;
    }
}
