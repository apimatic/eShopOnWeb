using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records a single SMS notification attempt for an order, including the state the
/// messaging provider owns (provider message id and current delivery outcome) so a
/// later request can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local lifecycle markers; once the provider accepts a message its own status
    // vocabulary is used (queued, scheduled, sent, delivered, failed, undelivered, canceled).
    public const string PendingStatus = "pending";
    public const string SendFailedStatus = "failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        string toNumber,
        NotificationType type,
        string body,
        DateTimeOffset? scheduledForUtc = null,
        string? idempotencyKey = null)
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
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = idempotencyKey;
        Status = PendingStatus;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }
    public string Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledForUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void MarkProviderAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void MarkSendFailed(string errorMessage, int? errorCode = null)
    {
        Status = SendFailedStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void UpdateFromProvider(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }
}
