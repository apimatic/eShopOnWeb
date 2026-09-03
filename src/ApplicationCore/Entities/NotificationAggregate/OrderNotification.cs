using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one SMS message the shop sent (or tried to send) to one recipient for one order event.
/// It carries enough of the state the provider owns — its message id and current delivery status — that a
/// later request (status refresh, resend, cancel of a scheduled message, content disposal) can act on it
/// and report on it, not only the request that created it. The <see cref="Recipient"/> is never logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string recipient,
        string body, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipient, nameof(recipient));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Recipient = recipient;
        Body = body;
        ScheduledFor = scheduledFor;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (order's buyer). Used to scope shopper-facing reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number (E.164). Persisted, but never written to logs.</summary>
    public string Recipient { get; private set; }

    /// <summary>The message text. Nulled once the shopper asks for its content to be disposed of.</summary>
    public string? Body { get; private set; }

    public NotificationState State { get; private set; } = NotificationState.Suppressed;

    /// <summary>The provider's identifier for this message (Twilio message SID), once one exists.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its own status wire value).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider, if this is one.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend request that produced this message, if any.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The provider accepted the message: record its id and initial status.</summary>
    public void MarkSent(string messageSid, string? providerStatus, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        State = NotificationState.Sent;
        ProviderMessageSid = messageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>The message could not be handed to the provider — record why, without a message id.</summary>
    public void MarkFailed(int? errorCode, string? errorMessage)
    {
        State = NotificationState.Failed;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh the provider-owned delivery outcome (from a later fetch of the message).</summary>
    public void UpdateProviderStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (providerStatus is not null)
        {
            ProviderStatus = providerStatus;
        }
        if (errorCode is not null)
        {
            ProviderErrorCode = errorCode;
        }
        if (errorMessage is not null)
        {
            ProviderErrorMessage = errorMessage;
        }
    }

    /// <summary>The message content has been disposed of at the provider; drop the local copy too.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void RecordResendKey(string idempotencyKey)
    {
        ResendIdempotencyKey = idempotencyKey;
    }
}
