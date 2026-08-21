using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        string destinationNumber,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }

    /// <summary>Provider-canonical E.164 destination. Never write this to logs.</summary>
    public string DestinationNumber { get; private set; }

    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }

    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }

    public void RecordProviderAccepted(string messageSid, string status, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode: null, errorMessage: null, dateSent);
    }

    public void RecordSendFailure(string status, int? errorCode, string? errorMessage)
    {
        ApplyProviderState(status, errorCode, errorMessage, dateSent: null);
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }

        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        ContentRedactedAt = DateTimeOffset.UtcNow;
    }

    public bool IsCancellableFollowUp()
    {
        if (Kind != OrderNotificationKind.DeliveryFollowUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return ProviderStatus is "scheduled" or "queued" or "accepted" or "pending";
    }

    public string ResolveBodyForResend()
    {
        if (!string.IsNullOrEmpty(Body))
        {
            return Body;
        }

        return OrderSmsTemplates.For(Kind, OrderId);
    }
}
