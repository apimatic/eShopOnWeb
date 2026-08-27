using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider-owned state (message identifier and delivery outcome)
/// so later requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal delivery outcomes reported by the provider; anything else may still change.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int? contactNumberId,
        OrderNotificationType type, string? body, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>
    /// The registered contact number this message went to. Null if the shopper had
    /// no usable number on file when the message was attempted.
    /// </summary>
    public int? ContactNumberId { get; private set; }

    public OrderNotificationType Type { get; private set; }

    /// <summary>
    /// The provider's identifier for the message (e.g. Twilio Message SID).
    /// Null if the message never reached the provider.
    /// </summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// The provider's current delivery outcome for the message
    /// (queued, scheduled, sent, delivered, undelivered, failed, canceled, ...),
    /// or a local value ("pending", "error") if the provider has none.
    /// </summary>
    public string Status { get; private set; }

    /// <summary>
    /// The text of the message. Cleared when the content is disposed of.
    /// </summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// For messages queued with the provider for later delivery, when it will go out.
    /// </summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void MarkSent(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = "error";
        ErrorMessage = errorMessage;
    }

    public void UpdateProviderStatus(string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));
        Status = providerStatus;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public bool HasTerminalStatus() => Array.Exists(TerminalStatuses, s => s == Status);
}
