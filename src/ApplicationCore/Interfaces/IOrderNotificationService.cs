using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendResult
{
    Sent,
    AlreadyProcessed,
    NotFound,
    DestinationRemoved,
    ContentRedacted
}

public class ResendResponse
{
    public ResendResult Result { get; set; }
    public OrderNotification? Notification { get; set; }
}

/// <summary>
/// Orchestrates order notifications. Messaging failures never propagate:
/// the underlying order operation always succeeds.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task<ResendResponse> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Redact a message's text at the provider and locally.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refresh non-terminal notifications' delivery outcome from the provider.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
}
