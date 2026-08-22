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
        OrderNotificationKind kind,
        string? body,
        string destinationCanonicalNumber,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationCanonicalNumber, nameof(destinationCanonicalNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationCanonicalNumber = destinationCanonicalNumber;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool BodyRedacted { get; private set; }
    public string DestinationCanonicalNumber { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void MarkScheduledFor(DateTimeOffset sendAt)
    {
        ScheduledFor = sendAt;
    }

    public void RecordProviderAcceptance(string? providerSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderSid = providerSid;
        Status = status ?? Status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RecordSendFailure(string errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;

        if (BodyRedacted)
        {
            Body = null;
            return;
        }

        if (body is not null)
        {
            Body = body;
        }
    }

    public void MarkBodyRedacted()
    {
        Body = null;
        BodyRedacted = true;
    }

    public bool DestinationMatches(string canonicalNumber) =>
        string.Equals(DestinationCanonicalNumber, canonicalNumber, StringComparison.Ordinal);
}
