using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification eShop asked the provider to send for an order,
/// carrying the provider's own identifier and latest known delivery outcome so later
/// requests can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Provider statuses that will not change anymore without further action.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string recipientNumber, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipientNumber, nameof(recipientNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        RecipientNumber = recipientNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = scheduledFor.HasValue ? "scheduled" : "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 number this message was addressed to.</summary>
    public string RecipientNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's latest known delivery status for the message.</summary>
    public string ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied key for operator-initiated resends; used to deduplicate retries.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(ProviderStatus);

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
