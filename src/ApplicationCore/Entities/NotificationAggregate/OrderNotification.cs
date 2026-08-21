using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        string? providerSid,
        string status,
        int? errorCode = null,
        string? errorMessage = null,
        DateTimeOffset? sendAt = null,
        DateTimeOffset? providerDateSent = null,
        int? originalNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(body, nameof(body));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ProviderSid = providerSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SendAt = sendAt;
        ProviderDateSent = providerDateSent;
        OriginalNotificationId = originalNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? body, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = dateSent ?? ProviderDateSent;

        if (ContentRedacted || body == string.Empty)
        {
            ContentRedacted = true;
            Body = string.Empty;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = string.Empty;
    }

    public bool IsScheduledFollowUp()
    {
        return Kind == NotificationKind.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderSid)
            && string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase);
    }
}
