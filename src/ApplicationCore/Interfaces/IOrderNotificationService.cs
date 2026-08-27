using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    Resent,
    AlreadyProcessed,
    NotFound,
    ContentDisposed,
    ContactNumberRemoved
}

public class ResendResult
{
    public ResendOutcome Outcome { get; set; }
    public OrderNotification? Notification { get; set; }
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Present both at the provider and locally.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Known to the provider (sent from this application's number) but not to eShop.</summary>
    public List<ReconciliationEntry> MissingLocally { get; set; } = new();

    /// <summary>Recorded by eShop but unknown to the provider for the range.</summary>
    public List<ReconciliationEntry> MissingAtProvider { get; set; } = new();
}

public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws; never fails the order.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues a delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order was cancelled and calls off any follow-up not yet sent.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Returns the notifications for an order, refreshing delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper, idempotently by caller-supplied key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content both locally and at the provider. False if unknown.</summary>
    Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines up the provider's record of messages for a range against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
