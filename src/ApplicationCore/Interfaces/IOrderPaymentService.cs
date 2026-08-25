using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize, fulfil (capture), cancel (void)
/// and refund. Ownership checks (a shopper may only act on their own order) are enforced here
/// for the shopper-scoped operations; fulfil/cancel are operator actions and act on any order
/// (the caller's admin role is enforced at the API boundary).
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shippingAddress, CancellationToken ct);

    Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);

    Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}
