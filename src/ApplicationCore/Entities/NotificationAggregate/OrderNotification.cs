using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single text message sent (or scheduled) to a shopper about an order.
/// Carries the provider's message identifier and the last known delivery outcome so a
/// later request can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationType notificationType, string body, DateTimeOffset? scheduledFor = null,
        int? resendOfId = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        NotificationType = notificationType;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfId = resendOfId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }

    /// <summary>Canonical destination the message was sent to. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationType NotificationType { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's message identifier (SID).</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The provider's last known delivery status (wire value, e.g. queued/sent/delivered/failed/undelivered/scheduled/canceled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Set for provider-scheduled messages; the UTC instant the provider will send it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>For operator re-sends: the notification this one repeats.</summary>
    public int? ResendOfId { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkSent(string providerMessageId, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        ProviderMessageId = providerMessageId;
        ProviderStatus = providerStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus ?? "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDeliveryOutcome(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
