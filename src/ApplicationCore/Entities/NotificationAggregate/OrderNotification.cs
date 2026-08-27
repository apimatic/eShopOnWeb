using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification attempt for an order, carrying the provider-owned
/// state (message identifier and delivery outcome) so later requests can act on it.
/// The destination number is PII: it must never be written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider never accepted the message at all.
    public const string SendFailedStatus = "send-failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message (wire value).</summary>
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>For provider-scheduled messages, when the provider will send it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Set on re-send attempts: the notification this one re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key for re-send attempts.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkAccepted(string providerMessageSid, string providerStatus, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string reason)
    {
        Status = SendFailedStatus;
        ProviderErrorMessage = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderState(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
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
