using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body, nameof(body));
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending_provider";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public string? ProviderDateUpdated { get; private set; }
    public string? ProviderPrice { get; private set; }
    public string? ProviderPriceUnit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public bool ContentDisposed { get; private set; }
    public bool CancellationPending { get; private set; }

    public void ApplyProviderState(ProviderMessageState state)
    {
        ProviderMessageSid = state.Sid ?? ProviderMessageSid;
        ProviderStatus = string.IsNullOrWhiteSpace(state.Status) ? ProviderStatus : state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        ProviderDateCreated = state.DateCreated;
        ProviderDateSent = state.DateSent;
        ProviderDateUpdated = state.DateUpdated;
        ProviderPrice = state.Price;
        ProviderPriceUnit = state.PriceUnit;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
        CancellationPending = false;
    }

    public void RecordProviderFailure(string safeMessage)
    {
        ProviderStatus = "provider_error";
        ProviderErrorMessage = safeMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void RequestCancellation() => CancellationPending = true;

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

public sealed record ProviderMessageState(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateCreated,
    string? DateSent,
    string? DateUpdated,
    string? Price,
    string? PriceUnit,
    string? From = null,
    string? To = null,
    string? Body = null);
