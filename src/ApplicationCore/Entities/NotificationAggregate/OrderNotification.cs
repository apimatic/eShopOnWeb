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
        string destinationNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? LocalFailure { get; private set; }

    public void RecordProviderAccepted(string sid, string? status, int? errorCode, string? errorMessage, DateTimeOffset? sendAt)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ApplyProviderState(status, errorCode, errorMessage);
        SendAt = sendAt;
        LocalFailure = null;
    }

    public void RecordLocalFailure(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        ProviderStatus = "not_sent";
        LocalFailure = reason;
    }

    public void ApplyProviderState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkResentFrom(int sourceNotificationId)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        ResentFromNotificationId = sourceNotificationId;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public override string ToString() => $"OrderNotification {Id}";
}
