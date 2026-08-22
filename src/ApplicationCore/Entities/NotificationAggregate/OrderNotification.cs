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
        int? contactNumberId,
        string destinationNumber,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void RecordProviderAccepted(string messageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode, errorMessage, Body);
    }

    public void RecordLocalSendFailure(string reason)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = reason;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? providerBody)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (providerBody != null && providerBody.Length == 0)
        {
            Body = null;
            ContentRedacted = true;
        }
        else if (providerBody != null)
        {
            Body = providerBody;
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public string ResolveBodyForResend(int orderId)
    {
        if (!string.IsNullOrEmpty(Body))
        {
            return Body;
        }

        return OrderNotificationTemplates.For(Kind, orderId);
    }
}
