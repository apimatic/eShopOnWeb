using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification attempt tied to an order, carrying the provider-owned state
/// (provider message id and latest known delivery outcome) so later requests can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Provider-reported terminal states (wire values); anything else is polled for refresh.
    public const string StatusSendFailed = "send-failed";

    private static readonly string[] TerminalStatuses =
        { "delivered", "failed", "undelivered", "canceled", StatusSendFailed };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string ToNumber { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's message identifier; null when the provider never accepted the send.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's delivery status (wire value), or "send-failed" when never accepted.</summary>
    public string? Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key; set on notifications produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one re-sends, when produced by a resend.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsInTerminalState => Status is not null && TerminalStatuses.Contains(Status);

    public void MarkAccepted(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        Touch();
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = StatusSendFailed;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        Status = status ?? Status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    public void MarkAsResend(int resendOfNotificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
