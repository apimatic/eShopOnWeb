using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of one SMS notification attempted for an order. Carries the provider's own
/// identifier and latest known delivery outcome so later requests can act on it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    // Local lifecycle states (provider wire statuses are stored verbatim once a message exists)
    public const string StatusPendingSend = "pending-send";
    public const string StatusSendFailed = "send-failed";
    public const string StatusSendOutcomeUnknown = "send-outcome-unknown";

    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(int orderId, string buyerId, string toNumber, NotificationType type, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null, int? resendOfNotificationId = null)
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
        Status = StatusPendingSend;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (null if the send never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Latest known delivery outcome: a provider wire status or a local lifecycle state.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; repeats under the same key do not re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a resend, the notification it re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public void MarkSent(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = StatusSendFailed;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSendOutcomeUnknown(string? errorMessage)
    {
        Status = StatusSendOutcomeUnknown;
        ProviderErrorMessage = errorMessage;
    }

    public void UpdateProviderState(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
