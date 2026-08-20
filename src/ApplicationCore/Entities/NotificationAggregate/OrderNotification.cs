using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Local record of an SMS relating to an order, including the provider's identifier
/// and last-known delivery outcome so later requests can act on and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string StatusNotSent = "not_sent";
    public const string StatusSendFailed = "send_failed";

    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationType type,
        string body,
        string destination,
        DateTimeOffset? scheduledAt = null,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destination, nameof(destination));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        Body = body;
        Destination = destination;
        ScheduledAt = scheduledAt;
        ResentFromNotificationId = resentFromNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = StatusNotSent;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationType Type { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public string Destination { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }

    public void ApplyProviderResult(
        string? sid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent,
        string? providerBody = null)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = dateSent;

        if (ContentRedacted)
        {
            return;
        }

        if (providerBody is not null && string.IsNullOrEmpty(providerBody))
        {
            RedactContent();
            return;
        }

        if (!string.IsNullOrEmpty(providerBody))
        {
            Body = providerBody;
        }
    }

    public void MarkSendFailed(string errorMessage)
    {
        ProviderStatus = StatusSendFailed;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsScheduled =>
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public static bool IsNonTerminal(string status) =>
        status is "queued" or "sending" or "sent" or "accepted" or "scheduled" or StatusNotSent;
}
