using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves through its lifecycle.
///
/// Every method here is best-effort: a messaging failure is recorded but never propagated, so the underlying
/// order operation (place / dispatch / cancel) always succeeds. A shopper with no number on file is simply not
/// messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Tell the shopper their order is on its way, and queue a delivery-feedback follow-up with the
    /// provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Tell the shopper their order was cancelled, and call off any delivery-feedback follow-up that has
    /// not yet gone out so it never reaches them.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);
}
