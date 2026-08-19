using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) to a shopper about one of their orders.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on it (resend, cancel, dispose) and report on it.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the shopper the message is about (order's buyer). Used for scoping.</summary>
    public string OwnerId { get; private set; }

    /// <summary>The order this notification relates to.</summary>
    public int OrderId { get; private set; }

    public SmsNotificationKind Kind { get; private set; }

    /// <summary>The destination number in E.164. Stored as data; must never be written to logs.</summary>
    public string Destination { get; private set; }

    /// <summary>The provider's identifier for the message (its "SID"). Null until the provider accepts it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome, mapped from the provider's own status.</summary>
    public SmsDeliveryStatus Status { get; private set; } = SmsDeliveryStatus.Pending;

    /// <summary>The raw provider status string, retained verbatim for diagnostics/reporting.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>The message body. Cleared locally (and redacted at the provider) on content disposal.</summary>
    public string? Content { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>For a queued follow-up: when the provider is due to send it.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True for a follow-up still sitting scheduled with the provider (callable-off).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When set, this notification was produced by re-sending an earlier one.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this row (if any).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When the provider first accepted the message (its <c>date_sent</c> is preferred once known).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedDate { get; private set; } = DateTimeOffset.UtcNow;

    private SmsNotification()
    {
        // Required by EF Core.
        OwnerId = null!;
        Destination = null!;
    }

    public SmsNotification(string ownerId, int orderId, SmsNotificationKind kind, string destination, string content)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Kind = kind;
        Destination = Guard.Against.NullOrEmpty(destination, nameof(destination));
        Content = content;
    }

    /// <summary>Marks this row as a resend of <paramref name="originalNotificationId"/> under <paramref name="idempotencyKey"/>.</summary>
    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Touch();
    }

    /// <summary>Records the provider's acceptance of an immediately-sent message.</summary>
    public void RecordSent(string providerMessageSid, string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderStatus = providerStatus;
        Status = SmsDeliveryStatusExtensions.FromProviderStatus(providerStatus);
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsScheduled = false;
        SentAt = sentAt ?? SentAt ?? DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>Records the provider's acceptance of a follow-up scheduled for a later time.</summary>
    public void RecordScheduled(string providerMessageSid, string? providerStatus, DateTimeOffset scheduledSendAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderStatus = providerStatus;
        Status = SmsDeliveryStatusExtensions.FromProviderStatus(providerStatus);
        if (Status == SmsDeliveryStatus.Pending)
            Status = SmsDeliveryStatus.Scheduled;
        ScheduledSendAt = scheduledSendAt;
        IsScheduled = true;
        Touch();
    }

    /// <summary>Records that the send request itself could not be handed to the provider.</summary>
    public void RecordSendFailure(string? errorMessage, int? errorCode = null)
    {
        Status = SmsDeliveryStatus.Failed;
        ProviderStatus ??= "failed";
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        IsScheduled = false;
        Touch();
    }

    /// <summary>
    /// Advances the delivery outcome from a fresh provider snapshot. A terminal status is
    /// never overwritten by a later, possibly stale, status.
    /// </summary>
    public void UpdateFromProvider(string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (Status.IsTerminal())
            return;

        ProviderStatus = providerStatus;
        Status = SmsDeliveryStatusExtensions.FromProviderStatus(providerStatus);
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (dateSent.HasValue)
            SentAt = dateSent;
        if (Status != SmsDeliveryStatus.Scheduled)
            IsScheduled = false;
        Touch();
    }

    /// <summary>Marks a previously-scheduled follow-up as called off before it went out.</summary>
    public void MarkCanceled(string? providerStatus = "canceled")
    {
        Status = SmsDeliveryStatus.Canceled;
        ProviderStatus = providerStatus;
        IsScheduled = false;
        Touch();
    }

    /// <summary>Disposes of the message text locally (the provider copy is redacted separately).</summary>
    public void RedactContent()
    {
        Content = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
