using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        string destinationE164,
        OrderNotificationKind kind,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationE164, nameof(destinationE164));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        DestinationE164 = destinationE164;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string DestinationE164 { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void MarkAsResendOf(int sourceNotificationId)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        SourceNotificationId = sourceNotificationId;
    }

    public void ApplyProviderState(
        string? messageSid,
        string? status,
        string? errorCode,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent)
    {
        if (!string.IsNullOrEmpty(messageSid))
        {
            ProviderMessageSid = messageSid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (dateCreated.HasValue)
        {
            ProviderDateCreated = dateCreated;
        }

        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
    }

    public void MarkSendFailed(string errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
