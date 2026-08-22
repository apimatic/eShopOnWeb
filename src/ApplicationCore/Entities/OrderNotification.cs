using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationNumber,
        OrderNotificationKind kind,
        string body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? scheduledFor,
        int? resentFromNotificationId,
        string? idempotencyKey)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = SanitizeProviderError(providerErrorMessage);
        ScheduledFor = scheduledFor;
        ResentFromNotificationId = resentFromNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);
        if (!ContentRedacted && body != null)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(string status, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);
    }

    public void AttachProviderMessage(string messageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = messageSid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public bool IsPendingFollowUp()
    {
        if (Kind != OrderNotificationKind.DeliveryFollowUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeProviderError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return errorMessage;
        }

        // Provider errors can echo the destination number; keep only a generic failure note in storage/logs.
        return "Provider reported a delivery error.";
    }
}
