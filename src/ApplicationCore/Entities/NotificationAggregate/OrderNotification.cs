using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        int? contactNumberId,
        string? destinationE164,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        DestinationE164 = destinationE164;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? DestinationE164 { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? SendFailure { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledFor = sendAt;
    }

    public void ApplyProviderResult(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            ProviderSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        SendFailure = null;
    }

    public void MarkSendFailed(string reason)
    {
        SendFailure = reason;
    }

    public void MarkAsResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResentFromNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsStillScheduled()
    {
        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
    }

    public bool DestinationStillRegistered(int? activeContactNumberId)
    {
        if (!ContactNumberId.HasValue)
        {
            return false;
        }

        return activeContactNumberId == ContactNumberId;
    }
}
