using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string NotSentStatus = "not_sent";
    public const string FailedStatus = "failed";

    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledAt = null,
        int? relatedNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ScheduledAt = scheduledAt;
        RelatedNotificationId = relatedNotificationId;
        ProviderStatus = NotSentStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public int? RelatedNotificationId { get; private set; }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public bool CanBeCancelledWithProvider()
    {
        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return ProviderStatus is "queued" or "scheduled" or "accepted" or "sending";
    }

    public void ApplyProviderResult(
        string? sid,
        string? status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent,
        string? providerBody)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }

        if (ContentRedacted)
        {
            Body = string.Empty;
            return;
        }

        if (providerBody == string.Empty && !string.IsNullOrEmpty(ProviderMessageSid))
        {
            RedactLocalContent();
        }
    }

    public void MarkFailed(string? errorMessage)
    {
        ProviderStatus = FailedStatus;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSkippedNoDestination()
    {
        ProviderStatus = NotSentStatus;
        ProviderErrorMessage = "Shopper has no contact number on file.";
    }

    public void RedactLocalContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }
}
