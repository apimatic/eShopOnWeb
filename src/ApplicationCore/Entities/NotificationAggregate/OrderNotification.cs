using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS notification handed (or attempted) to the messaging provider
/// for an order. Carries the provider's own state (message SID, delivery outcome) so a
/// later request can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, string toNumber, NotificationType type,
        string body, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }

    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when the message never reached the provider at all (send threw).</summary>
    public bool SendFailed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator resends; a repeat under the same key must not resend.</summary>
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void MarkHandedToProvider(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        SendFailed = false;
    }

    public void MarkSendFailed()
    {
        SendFailed = true;
    }

    public void UpdateProviderOutcome(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
