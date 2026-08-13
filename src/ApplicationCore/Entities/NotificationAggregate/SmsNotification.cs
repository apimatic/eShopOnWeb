using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop tried to send a shopper about one of their orders. It carries
/// enough of the state the provider owns — its message identifier and the current delivery outcome —
/// that a later request can act on the message (resend, cancel, dispose of its content) and report on
/// it, not merely the request that first sent it.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }

    public SmsNotification(
        string ownerId,
        int orderId,
        NotificationKind kind,
        string toPhoneNumber,
        string body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Identity of the shopper the message is about. Used to scope shopper-facing reads.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The order this message relates to.</summary>
    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The canonical E.164 destination. Persisted (not logged) so a resend knows where to go.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The text sent. Cleared once its content is disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (e.g. a Twilio Message SID), once created.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The last delivery outcome the provider reported for this message.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When a scheduled message (the delivery follow-up) is due to go out, if any.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The provider timestamp of when the message was actually sent, if known.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True once the message text has been disposed of at both the provider and here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this record, when it was a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>If this record is a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>
    /// A message that never reached the provider (no identifier was returned). Everything created
    /// with a provider identifier is at least accepted; a null identifier means the send itself failed.
    /// </summary>
    public bool WasCreatedWithProvider => !string.IsNullOrEmpty(ProviderSid);

    /// <summary>
    /// True once the provider's outcome is final and no further change is expected, so status refresh
    /// can stop polling the provider for it.
    /// </summary>
    public bool IsTerminal =>
        ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "read";

    /// <summary>Records the outcome of first handing the message to the provider.</summary>
    public void RecordProviderResult(string? providerSid, string? status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        ProviderSid = providerSid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (sentAt.HasValue) SentAt = sentAt;
    }

    /// <summary>Records that the send could not even be handed to the provider.</summary>
    public void RecordSendFailure(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Updates the last-known delivery outcome from a later look at the provider's record.</summary>
    public void UpdateDeliveryOutcome(string? status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        if (!string.IsNullOrEmpty(status)) ProviderStatus = status;
        if (errorCode.HasValue) ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ProviderErrorMessage = errorMessage;
        if (sentAt.HasValue) SentAt = sentAt;
    }

    /// <summary>Marks the content disposed and drops the local copy of the text.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}
