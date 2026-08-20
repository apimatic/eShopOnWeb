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
        NotificationKind kind,
        string? providerSid,
        string? status,
        string body,
        string destinationNumber,
        string? fromNumber,
        string? messagingServiceSid,
        string? direction,
        string? dateCreated,
        string? dateSent,
        string? dateUpdated,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? sendAt,
        int? resentFromNotificationId,
        string? idempotencyKey)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ProviderSid = providerSid;
        Status = status;
        Body = body;
        DestinationNumber = destinationNumber;
        FromNumber = fromNumber;
        MessagingServiceSid = messagingServiceSid;
        Direction = direction;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        ProviderDateUpdated = dateUpdated;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SendAt = sendAt;
        ResentFromNotificationId = resentFromNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? FromNumber { get; private set; }
    public string? MessagingServiceSid { get; private set; }
    public string? Direction { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public string? ProviderDateUpdated { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ApplyProviderState(
        string? providerSid,
        string? status,
        string? body,
        string? fromNumber,
        string? messagingServiceSid,
        string? direction,
        string? dateCreated,
        string? dateSent,
        string? dateUpdated,
        int? errorCode,
        string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerSid))
        {
            ProviderSid = providerSid;
        }

        Status = status;
        FromNumber = fromNumber ?? FromNumber;
        MessagingServiceSid = messagingServiceSid ?? MessagingServiceSid;
        Direction = direction ?? Direction;
        ProviderDateCreated = dateCreated ?? ProviderDateCreated;
        ProviderDateSent = dateSent ?? ProviderDateSent;
        ProviderDateUpdated = dateUpdated ?? ProviderDateUpdated;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;

        if (ContentRedacted)
        {
            Body = null;
        }
        else if (body != null)
        {
            Body = body;
        }
    }

    public void MarkLocalFailure(string errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }
}
