using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS sent (or attempted) to a shopper about an order, carrying the
/// provider-owned state (message identifier and latest known delivery outcome)
/// so later requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider never accepted the message.
    public const string SendFailedStatus = "SendFailed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
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
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = scheduledFor.HasValue ? NotificationStatus.Scheduled : NotificationStatus.Queued;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>Null once the shopper removes the number; the history record survives.</summary>
    public int? ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's message identifier (null when the send never got accepted).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Latest known provider delivery outcome (queued/sent/delivered/...) or SendFailed.</summary>
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Caller-supplied key for operator resends; repeats under the same key do not resend.</summary>
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(providerStatus) ? Status : providerStatus;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = SendFailedStatus;
        ProviderErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderState(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The contact number was removed; keep history but block any future send.</summary>
    public void DetachContactNumber()
    {
        ContactNumberId = null;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}
