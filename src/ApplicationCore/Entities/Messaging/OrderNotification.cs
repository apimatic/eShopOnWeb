using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt = null,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        SendAt = sendAt;
        ResentFromNotificationId = resentFromNotificationId;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }

    public void ApplyProviderState(string? sid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
    }

    public void MarkSendFailed(string status = "failed")
    {
        ProviderStatus = status;
    }

    public void RedactBody()
    {
        Body = null;
        ContentRedacted = true;
    }
}
