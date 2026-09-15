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
        OrderNotificationKind kind,
        string? body,
        DateTimeOffset? scheduledSendAt = null,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? DateCreated { get; private set; }
    public string? DateSent { get; private set; }
    public string? DateUpdated { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void ApplyProviderState(
        string? sid,
        string? status,
        int? errorCode,
        string? errorMessage,
        string? dateCreated,
        string? dateSent,
        string? dateUpdated,
        string? body)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            ProviderSid = sid;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateCreated = dateCreated;
        DateSent = dateSent;
        DateUpdated = dateUpdated;

        if (!ContentRedacted && body is not null)
        {
            Body = body;
        }
    }

    public void MarkLocalFailure(string message)
    {
        Status = "failed";
        ErrorMessage = message;
    }

    public void MarkContentRedacted(string? providerBody)
    {
        ContentRedacted = true;
        Body = null;
        if (providerBody is not null)
        {
            DateUpdated = DateUpdated;
        }
    }

    public bool IsScheduledFollowUpOutstanding()
    {
        if (Kind != OrderNotificationKind.DispatchFollowUp)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProviderSid))
        {
            return false;
        }

        var status = Status ?? string.Empty;
        return status.Equals("scheduled", StringComparison.OrdinalIgnoreCase)
               || status.Equals("queued", StringComparison.OrdinalIgnoreCase)
               || status.Equals("accepted", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTerminalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Equals("delivered", StringComparison.OrdinalIgnoreCase)
               || status.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
               || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
               || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
               || status.Equals("received", StringComparison.OrdinalIgnoreCase)
               || status.Equals("read", StringComparison.OrdinalIgnoreCase);
    }
}
