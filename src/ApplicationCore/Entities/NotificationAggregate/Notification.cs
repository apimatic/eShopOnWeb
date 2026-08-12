using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message eShop tried to send to a shopper about one of their orders. It records
/// enough of the state the provider owns — the provider message identifier and the current
/// delivery outcome — that a later request can act on the message (resend, redact, cancel) and
/// report on it, not just the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(
        string buyerId,
        int? orderId,
        NotificationKind kind,
        string toNumber,
        string body,
        string? idempotencyKey = null,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(body, nameof(body));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IdempotencyKey = idempotencyKey;
        ScheduledSendAt = scheduledSendAt;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the message; scopes shopper visibility. Holds the buyer's username.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order this message concerns, when applicable.</summary>
    public int? OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number in canonical E.164. Persisted but never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (Twilio message SID), when it has one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; null for all other notifications.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When a scheduled (follow-up) message is due to go out at the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the provider's response to a create/schedule call.</summary>
    public void ApplyProviderResult(string? providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }
        Status = string.IsNullOrEmpty(status) ? Status : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Updates the delivery outcome from a later provider fetch.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Marks that the message could not be handed to the provider at all.</summary>
    public void MarkSendFailed(string reason)
    {
        Status = NotificationStatus.SendFailed;
        ProviderErrorMessage = reason;
    }

    /// <summary>Marks a scheduled message as called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
    }

    /// <summary>Disposes of the stored content locally; the provider copy is redacted separately.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
