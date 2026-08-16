using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item and quantity requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund — each separately invocable.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for the shopper from catalog items. Amounts come from catalog prices.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken ct);

    /// <summary>Authorize the order total against a one-off card or a saved card. Idempotent per order.</summary>
    Task<PaymentView> PayOrderAsync(string buyerId, int orderId, CardPaymentDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Operator: fulfil the order, capturing the held funds. Renews a stale authorization if needed.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured payment for the shopper's own order, in full or in part. Idempotent per key.</summary>
    Task<(string RefundId, PaymentView Payment)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
}
