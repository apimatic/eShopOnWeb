using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string shopperId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null)
    {
        OrderId = orderId;
        ShopperId = shopperId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string ShopperId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool ContentDisposed { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderFailure(int? errorCode)
    {
        ProviderStatus = "provider_error";
        ProviderErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderOutcomeUnknown()
    {
        ProviderStatus = "provider_outcome_unknown";
        ProviderErrorCode = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancellationPending()
    {
        ProviderStatus = "cancellation_pending";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}
