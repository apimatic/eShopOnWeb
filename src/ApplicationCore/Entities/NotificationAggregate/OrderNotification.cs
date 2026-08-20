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
        string kind,
        string body,
        string destinationNumber,
        DateTimeOffset? scheduledForUtc = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        Status = LocalStatuses.Pending;
        CreatedUtc = DateTimeOffset.UtcNow;
        ScheduledForUtc = scheduledForUtc;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public bool ContentDisposed { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset? LastSyncedUtc { get; private set; }
    public DateTimeOffset? ScheduledForUtc { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void ApplyProviderState(
        string? providerSid,
        string status,
        int? errorCode,
        string? errorMessage,
        string? bodyFromProvider)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrEmpty(providerSid))
        {
            ProviderSid = providerSid;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedUtc = DateTimeOffset.UtcNow;

        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (bodyFromProvider != null)
        {
            Body = bodyFromProvider;
        }
    }

    public void MarkSendFailed(string reason)
    {
        Status = LocalStatuses.SendFailed;
        ErrorMessage = reason;
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void DisposeContent()
    {
        ContentDisposed = true;
        Body = null;
    }

    public static class LocalStatuses
    {
        public const string Pending = "pending";
        public const string SendFailed = "send_failed";
        public const string SkippedNoNumber = "skipped_no_number";
    }
}
