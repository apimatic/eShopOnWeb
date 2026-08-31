using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records a single SMS notification attempt for an order, together with the
/// state the messaging provider owns (message id and delivery outcome) so a
/// later request can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Provider (Twilio) terminal statuses, after which no further delivery change is expected.
    private static readonly string[] TerminalProviderStatuses = { "delivered", "failed", "undelivered", "canceled" };

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? MessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? IdempotencyKey { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationType type, string body, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    public bool IsTerminal => ProviderStatus != null &&
        Array.Exists(TerminalProviderStatuses, s => s == ProviderStatus);

    public bool IsScheduled => ProviderStatus == "scheduled";

    public void MarkAccepted(string messageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        ProviderStatus = providerStatus;
        if (providerStatus != "scheduled")
        {
            SentAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkFailed(int? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void UpdateFromProvider(string? status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            SentAt = dateSent;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
