using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) for an order, plus the state the provider owns for it:
/// the provider's own identifier and the current delivery outcome. That state is what lets a later
/// request act on the message (re-send, cancel, dispose its content) and report on it — not only the
/// request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    // Local status markers used before/instead of a provider status. Any other value stored in
    // <see cref="ProviderStatus"/> is the provider's own verbatim status string.
    public const string StatusSendFailed = "send_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner (the order's shopper). Used to scope shopper-facing reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (canonical E.164). Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (its message SID), when one was obtained.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome — the provider's own status string, or a local marker.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Provider error code when the message failed or was undelivered.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>Caller-supplied idempotency key for a re-send, so a repeat under the same key sends nothing new.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a scheduled (follow-up) message is queued to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the shopper has asked for this message's content to be disposed of.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Records the provider identifier and initial outcome obtained when the message was accepted.</summary>
    public void SetProviderResult(string providerMessageSid, string? providerStatus, int? providerErrorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>Records that the message could not even be handed to the provider. Does not fail the order.</summary>
    public void MarkSendFailed() => ProviderStatus = StatusSendFailed;

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateProviderState(string? providerStatus, int? providerErrorCode)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
    }

    public void SetScheduledSendAt(DateTimeOffset sendAt) => ScheduledSendAt = sendAt;

    public void SetIdempotencyKey(string idempotencyKey) => IdempotencyKey = idempotencyKey;

    /// <summary>
    /// Disposes of the message content locally. The provider-side redaction is performed by the
    /// notification service; this clears the text held here while the record of the message survives.
    /// </summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>True when this outcome will not change on its own and need not be re-fetched from the provider.</summary>
    public bool IsTerminal()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read" or StatusSendFailed;
    }
}
