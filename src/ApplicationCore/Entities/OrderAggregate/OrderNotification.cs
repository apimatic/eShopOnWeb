using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationNumber,
        string body,
        string? providerMessageSid,
        string providerStatus,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
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
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void ApplyProviderState(string? sid, string status, string? errorCode, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }
}
