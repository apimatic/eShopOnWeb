using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public sealed class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        string destination,
        NotificationKind kind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        ContactNumberId = contactNumberId;
        Destination = Guard.Against.NullOrWhiteSpace(destination);
        Kind = kind;
        Content = Guard.Against.NullOrWhiteSpace(content);
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Content { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public string? RefreshDiagnostic { get; private set; }
    public bool CancellationRequested { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }

    public void ApplyProviderSnapshot(ProviderMessageSnapshot snapshot, DateTimeOffset refreshedAt)
    {
        ProviderMessageId = snapshot.ProviderMessageId ?? ProviderMessageId;
        ProviderStatus = snapshot.Status ?? ProviderStatus;
        ProviderErrorCode = snapshot.ErrorCode;
        ProviderErrorMessage = snapshot.ErrorMessage;
        ProviderCreatedAt = snapshot.CreatedAt ?? ProviderCreatedAt;
        ProviderSentAt = snapshot.SentAt ?? ProviderSentAt;
        ProviderUpdatedAt = snapshot.UpdatedAt ?? ProviderUpdatedAt;
        LastRefreshedAt = refreshedAt;
        RefreshDiagnostic = null;
        if (string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            CancellationRequested = false;
        }
    }

    public void MarkProviderFailure(string diagnostic, DateTimeOffset attemptedAt)
    {
        ProviderStatus = ProviderMessageId is null ? "provider_failure" : ProviderStatus;
        RefreshDiagnostic = diagnostic;
        LastRefreshedAt = attemptedAt;
    }

    public void RequestCancellation() => CancellationRequested = true;

    public void ClearCancellationRequest() => CancellationRequested = false;

    public void MarkContentDisposed(DateTimeOffset disposedAt)
    {
        Content = null;
        ContentDisposedAt = disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}

public sealed record ProviderMessageSnapshot(
    string? ProviderMessageId,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? UpdatedAt,
    string? From,
    string? To,
    string? Body,
    string? MessagingServiceSid);
