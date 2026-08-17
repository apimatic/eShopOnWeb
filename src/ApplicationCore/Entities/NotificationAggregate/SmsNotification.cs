using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single text message about an order, together with the provider state eShop needs in order
/// to act on it and report on it later: the provider's message identifier and its current
/// delivery outcome. The record of a message — that it was sent and what became of it — outlives
/// the message body, which can be disposed of on request.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
#pragma warning restore CS8618

    public SmsNotification(
        int orderId,
        string buyerId,
        string toNumber,
        string body,
        NotificationKind kind,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Body = body;
        Kind = kind;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResentFromNotificationId = resentFromNotificationId;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order this message is about; used to keep one shopper's data from another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public NotificationKind Kind { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>The provider's own identifier for this message (its message SID), once created.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The raw provider status string, kept verbatim for fidelity.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When set, the message is queued with the provider to go out at this time (a follow-up).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public bool ContentRedacted { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this message (resend only).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>If this message is a re-send, the id of the message it re-sent.</summary>
    public int? ResentFromNotificationId { get; private set; }

    /// <summary>Records the outcome of handing the message to the provider.</summary>
    public void RecordSendResult(
        string? providerMessageId,
        NotificationStatus status,
        string? providerStatus,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? sentAt = null)
    {
        ProviderMessageId = providerMessageId;
        Status = status;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt.HasValue) SentAt = sentAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Advances the mirrored delivery outcome from a fresh read of the provider's record.</summary>
    public void UpdateDeliveryState(
        NotificationStatus status,
        string? providerStatus,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? sentAt = null)
    {
        Status = status;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt.HasValue) SentAt = sentAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the local record as cancelled after the provider cancelled a not-yet-sent message.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        ProviderStatus = "canceled";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Disposes of the message content locally. The fact of the message and its outcome survive;
    /// only the text is dropped. Callers redact the provider copy separately.
    /// </summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }
}
