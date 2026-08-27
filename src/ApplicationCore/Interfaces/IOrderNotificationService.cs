using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Tells shoppers by text message as their order moves. Every method is best-effort:
/// a message that cannot be sent is recorded and logged, never thrown — the underlying
/// order operation must always succeed.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);
}
