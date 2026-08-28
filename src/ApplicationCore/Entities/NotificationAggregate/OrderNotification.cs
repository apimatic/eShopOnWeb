using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed class OrderNotification : IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        Guid contactNumberId,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        Guid? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Content = content;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        SubmissionStatus = NotificationSubmissionStatus.Pending;
    }

    public Guid Id { get; private set; }
    public int OrderId { get; private set; }
    public Guid ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public Guid? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public NotificationSubmissionStatus SubmissionStatus { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderFrom { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public bool LastRefreshSucceeded { get; private set; }
    public ProviderActionState CancellationState { get; private set; }
    public ProviderActionState RedactionState { get; private set; }
    public byte[]? RowVersion { get; private set; }

    public void RecordProviderState(
        string? sid,
        string? from,
        string? status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent,
        DateTimeOffset? dateUpdated,
        DateTimeOffset refreshedAt)
    {
        ProviderSid = string.IsNullOrWhiteSpace(sid) ? ProviderSid : sid;
        ProviderFrom = from;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderCreatedAt = dateCreated;
        ProviderSentAt = dateSent;
        ProviderUpdatedAt = dateUpdated;
        LastRefreshedAt = refreshedAt;
        LastRefreshSucceeded = true;
        SubmissionStatus = NotificationSubmissionStatus.Accepted;
    }

    public void RecordFailure(string? errorCode, string safeMessage, bool ambiguous, DateTimeOffset attemptedAt)
    {
        ProviderErrorMessage = safeMessage;
        ProviderStatus = null;
        ProviderErrorCode = int.TryParse(errorCode, out var parsed) ? parsed : null;
        LastRefreshedAt = attemptedAt;
        LastRefreshSucceeded = false;
        SubmissionStatus = ambiguous ? NotificationSubmissionStatus.Ambiguous : NotificationSubmissionStatus.Rejected;
    }

    public void RecordConfigurationFailure(string safeMessage, DateTimeOffset attemptedAt)
    {
        ProviderErrorMessage = safeMessage;
        LastRefreshedAt = attemptedAt;
        LastRefreshSucceeded = false;
        SubmissionStatus = NotificationSubmissionStatus.Rejected;
    }

    public void MarkRefreshFailed(DateTimeOffset attemptedAt)
    {
        LastRefreshedAt = attemptedAt;
        LastRefreshSucceeded = false;
    }

    public void RequestCancellation() => CancellationState = ProviderActionState.Pending;
    public void ConfirmCancellation(string? status, DateTimeOffset confirmedAt)
    {
        CancellationState = ProviderActionState.Confirmed;
        ProviderStatus = status;
        LastRefreshedAt = confirmedAt;
        LastRefreshSucceeded = true;
    }

    public void RequestRedaction()
    {
        Content = null;
        RedactionState = ProviderActionState.Pending;
    }

    public void ConfirmRedaction(DateTimeOffset confirmedAt)
    {
        Content = null;
        RedactionState = ProviderActionState.Confirmed;
        LastRefreshedAt = confirmedAt;
        LastRefreshSucceeded = true;
    }
}
