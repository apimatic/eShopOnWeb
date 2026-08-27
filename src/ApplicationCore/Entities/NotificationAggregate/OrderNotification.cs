using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or scheduled) for an order,
/// carrying the provider-owned state (message identifier and delivery outcome)
/// so later requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Delivery statuses reported by the provider that we track locally.
    public const string StatusSendFailed = "send-failed"; // provider rejected the send request itself

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string ownerId, OrderNotificationType type, string toNumber, string body,
        DateTimeOffset? scheduledFor = null, int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }

    /// <summary>The identity (username) of the shopper the message is sent to.</summary>
    public string OwnerId { get; private set; }

    public OrderNotificationType Type { get; private set; }

    /// <summary>Canonical E.164 destination number at the time of sending.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome; mirrors the provider's message status once known.</summary>
    public string Status { get; private set; }

    public string? ProviderErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>For messages queued with the provider for later delivery, when it will go out.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Set when this notification was produced by re-sending an earlier one.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkSent(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
    }

    public void MarkSendFailed(string? providerErrorCode)
    {
        Status = StatusSendFailed;
        ProviderErrorCode = providerErrorCode;
    }

    public void UpdateProviderStatus(string providerStatus, string? providerErrorCode)
    {
        Status = providerStatus;
        ProviderErrorCode = providerErrorCode;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Statuses after which the provider will no longer change the outcome.</summary>
    public static bool IsTerminalStatus(string status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled" or StatusSendFailed;
}
