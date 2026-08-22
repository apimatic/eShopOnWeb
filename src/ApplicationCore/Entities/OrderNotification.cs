using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string? destinationCanonical,
        string? body)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationCanonical = destinationCanonical;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? DestinationCanonical { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }

    public bool IsScheduledPending =>
        !string.IsNullOrEmpty(ProviderSid) &&
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public void RecordProviderResult(
        string? providerSid,
        string? providerStatus,
        int? errorCode,
        string? errorMessage,
        string? body,
        DateTimeOffset? sendAt = null)
    {
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;

        if (ContentRedacted || body == string.Empty)
        {
            Body = null;
            ContentRedacted = true;
        }
        else if (body != null)
        {
            Body = body;
        }

        if (sendAt.HasValue)
        {
            SendAt = sendAt;
        }
    }

    public void RecordSendFailure(string safeError)
    {
        ProviderStatus = "failed";
        ErrorMessage = safeError;
    }

    public void MarkResentFrom(int originalNotificationId)
    {
        ResentFromNotificationId = originalNotificationId;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public string ResolveBodyForResend()
    {
        if (!ContentRedacted && !string.IsNullOrEmpty(Body))
        {
            return Body;
        }

        return OrderNotificationMessages.ForKind(Kind, OrderId, 0m);
    }
}
