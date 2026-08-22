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
        string destinationNumber,
        NotificationKind kind,
        string body,
        int? contactNumberId = null,
        int? sourceNotificationId = null,
        DateTimeOffset? sendAt = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        SourceNotificationId = sourceNotificationId;
        SendAt = sendAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? LocalFailure { get; private set; }

    public void RecordProviderAccepted(string messageSid, string? status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void RecordLocalFailure(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        LocalFailure = reason;
        ProviderStatus = "failed";
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

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }
}
