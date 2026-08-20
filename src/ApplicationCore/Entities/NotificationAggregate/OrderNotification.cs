using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset DateCreated { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DateScheduled { get; private set; }
    public bool BodyRedacted { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string destinationPhoneNumber,
        string? providerSid,
        string providerStatus,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateScheduled,
        int? parentNotificationId,
        string? idempotencyKey)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationPhoneNumber = destinationPhoneNumber;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateCreated = DateTimeOffset.UtcNow;
        DateScheduled = dateScheduled;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = SanitizeProviderError(errorMessage);
        if (BodyRedacted)
        {
            Body = string.Empty;
        }
        else if (body != null)
        {
            Body = body;
        }
    }

    public void MarkBodyRedacted()
    {
        Body = string.Empty;
        BodyRedacted = true;
    }

    private static string? SanitizeProviderError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return errorMessage;
        }

        // Provider error text can echo the destination number; keep a code-only note in logs,
        // but persist a shortened non-numeric description for operators.
        var trimmed = errorMessage.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
