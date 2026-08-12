using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The caller's orders, plus the notifications for one order. Delivery outcomes are refreshed
/// from the provider on read.</summary>
public record OrderNotificationsResult(ActionOutcome Outcome, IReadOnlyList<NotificationView> Notifications);

/// <summary>Shopper-scoped reads over a caller's own orders and notifications.</summary>
public interface IOrderQueryService
{
    /// <summary>The caller's orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What was sent for one order and what became of each message. Visible to the order's owner and to
    /// operators; other shoppers get <see cref="ActionOutcome.Forbidden"/>.
    /// </summary>
    Task<OrderNotificationsResult> GetOrderNotificationsAsync(
        int orderId, string requestingBuyerId, bool isAdministrator, CancellationToken cancellationToken = default);
}
