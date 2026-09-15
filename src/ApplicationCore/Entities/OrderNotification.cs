using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        int? contactNumberId,
        string destinationNumber,
        int? originalNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        OriginalNotificationId = originalNotificationId;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ContactNumberId { get; private set; }

    /// <summary>
    /// Destination in E.164. Never write this value to logs.
    /// </summary>
    public string DestinationNumber { get; private set; }

    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void ApplyProviderAcceptance(string sid, string status, string? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplySendFailure(string status, string? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, string? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (!ContentRedacted && body != null)
        {
            Body = body;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
