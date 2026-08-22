using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        int? contactNumberId,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderStatusFetchedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? LocalFailureReason { get; private set; }

    public void AttachProviderResult(string? sid, string? status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledFor)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ScheduledFor = scheduledFor;
        ProviderStatusFetchedAt = DateTimeOffset.UtcNow;
    }

    public void RecordLocalFailure(string reason)
    {
        LocalFailureReason = reason;
        ProviderStatus ??= "failed";
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? providerBody)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderStatusFetchedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            return;
        }

        if (providerBody == string.Empty)
        {
            RedactLocalContent();
            return;
        }

        if (!string.IsNullOrEmpty(providerBody))
        {
            Body = providerBody;
        }
    }

    public void MarkAsResendOf(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void RedactLocalContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public string ResolveBodyForResend()
    {
        if (!ContentRedacted && !string.IsNullOrEmpty(Body))
        {
            return Body;
        }

        return OrderNotificationTemplates.For(Kind, OrderId);
    }
}
