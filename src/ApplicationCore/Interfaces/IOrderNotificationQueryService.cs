using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationQueryService
{
    /// <summary>
    /// Returns the notifications for an order owned by the given buyer, refreshing each
    /// message's delivery outcome from the provider on a best-effort basis. Returns null
    /// when the order does not exist or belongs to someone else.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetForOrderAsync(int orderId, string buyerId, CancellationToken ct = default);
}
