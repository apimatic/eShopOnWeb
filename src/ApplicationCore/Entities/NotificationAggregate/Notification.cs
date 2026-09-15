using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message sent (or scheduled) to a shopper about one of their orders.
/// <para>
/// The notification carries enough of the state the messaging provider owns — the provider's own
/// message identifier (<see cref="ProviderMessageSid"/>) and its current delivery outcome
/// (<see cref="ProviderStatus"/>) — that a later request can act on the message (resend, cancel,
/// dispose of its content) and report on it, not only the request that first sent it.
/// </para>
/// <para><see cref="ToNumber"/> is the shopper's mobile number and is treated as PII: it is never logged.</para>
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>The order this notification is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (and the destination number).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination phone number, in the provider's canonical E.164 form. PII — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of (<see cref="ContentRedacted"/>).</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message, once the provider has accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The provider error code if the message failed or was undelivered; otherwise null.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>The provider error description if the message failed or was undelivered; otherwise null.</summary>
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>
    /// Set when the send attempt could not be handed to the provider at all (e.g. a transport error).
    /// PII-free. When set, <see cref="ProviderMessageSid"/> is null.
    /// </summary>
    public string? LocalError { get; private set; }

    /// <summary>For scheduled follow-ups, the time the provider was asked to send the message.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The provider accepted the message. Records its identifier and initial status.</summary>
    public void RecordProviderAccepted(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        LocalError = null;
    }

    /// <summary>Refresh the provider-owned delivery state for this message.</summary>
    public void UpdateProviderStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>The message could not be handed to the provider. The underlying business operation still succeeds.</summary>
    public void RecordLocalFailure(string reason)
    {
        LocalError = reason;
    }

    /// <summary>Dispose of the message content locally (the caller is responsible for redacting it at the provider first).</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>
    /// The overall outcome to report to a caller: the provider's status when the provider has the message,
    /// otherwise a local marker describing why nothing was sent.
    /// </summary>
    public string EffectiveStatus =>
        ProviderStatus ?? (LocalError is null ? "pending" : "not_sent");

    /// <summary>Whether the provider status is final (no further delivery transition is expected).</summary>
    public bool IsProviderStatusTerminal => MessageStatus.IsTerminal(ProviderStatus);

    /// <summary>Whether this message is a candidate for an operator resend (it did not reach the shopper).</summary>
    public bool CanBeResent =>
        LocalError is not null || MessageStatus.IsUndelivered(ProviderStatus);
}
