using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification attempted for an order, carrying the
/// provider-owned state (message identifier and delivery outcome) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationType type, string messageBody, DateTimeOffset? scheduledFor = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(messageBody, nameof(messageBody));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        MessageBody = messageBody;
        ScheduledFor = scheduledFor;
        ResendIdempotencyKey = resendIdempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The text that was sent. Null once the content has been disposed of.</summary>
    public string? MessageBody { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for the message (e.g. Twilio SM.../MM... SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued/sent/delivered/undelivered/failed/scheduled/canceled...),
    /// or a local value (pending/error) when the provider never accepted the message.</summary>
    public string Status { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Caller-supplied idempotency key; set only on notifications produced by a resend.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    public void MarkProviderAccepted(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string status, string? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderStatus(string status, string? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        MessageBody = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
