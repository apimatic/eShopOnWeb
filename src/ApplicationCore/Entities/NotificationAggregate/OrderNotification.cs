using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationNumber,
        string body,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderError { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void RecordScheduled(DateTimeOffset sendAt)
    {
        ScheduledFor = sendAt;
    }

    public void RecordProviderAcceptance(string sid, string status, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderSid = sid;
        ApplyProviderOutcome(status, null, null, dateSent);
    }

    public void RecordLocalFailure(string sanitizedError)
    {
        ProviderStatus = "failed";
        ProviderError = sanitizedError;
    }

    public void ApplyProviderOutcome(string status, int? errorCode, string? sanitizedError, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(sanitizedError))
        {
            ProviderError = sanitizedError;
        }
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }
}
