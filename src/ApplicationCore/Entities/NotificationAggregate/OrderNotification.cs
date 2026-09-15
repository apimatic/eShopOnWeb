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
        string body,
        int? contactNumberId,
        string destinationPhoneNumber,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        DestinationPhoneNumber = destinationPhoneNumber;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public bool IsScheduledFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp &&
        !string.IsNullOrEmpty(ProviderMessageSid) &&
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public bool DidNotReachShopper
    {
        get
        {
            if (string.IsNullOrEmpty(ProviderMessageSid))
            {
                return true;
            }

            return ProviderStatus is not null &&
                   (ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                    ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
                    ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool HasTerminalProviderStatus =>
        ProviderStatus is not null &&
        (ProviderStatus.Equals("delivered", StringComparison.OrdinalIgnoreCase) ||
         ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
         ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
         ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
         ProviderStatus.Equals("read", StringComparison.OrdinalIgnoreCase));

    public void SetScheduledSendAt(DateTimeOffset sendAt) => ScheduledSendAt = sendAt;

    public void RecordProviderAccepted(string providerMessageSid, string? status, string? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? "accepted" : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);
    }

    public void RecordProviderFailure(string? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);
    }

    public void ApplyProviderSnapshot(string? status, string? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = SanitizeProviderError(errorMessage);

        if (!ContentRedacted && body is not null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    private static string? SanitizeProviderError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return errorMessage;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            errorMessage,
            @"\+?\d[\d\s\-().]{7,}\d",
            "[redacted]");
    }
}
