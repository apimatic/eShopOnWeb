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
        int? contactNumberId,
        string destinationNumber,
        NotificationKind kind,
        string body,
        string? providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? sendAt,
        int? sourceNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        SendAt = sendAt;
        SourceNotificationId = sourceNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void ApplyProviderState(string? sid, string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;

        if (ContentRedacted)
        {
            Body = string.Empty;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsTerminalProviderStatus()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return false;
        }

        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
    }

    public bool IsPendingSend()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return !string.IsNullOrEmpty(ProviderMessageSid);
        }

        return ProviderStatus is "scheduled" or "queued" or "accepted" or "sending";
    }
}
