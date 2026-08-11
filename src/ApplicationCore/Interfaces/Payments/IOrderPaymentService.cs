using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Orchestrates the money flow over the existing order model: place, authorize (pay), capture (fulfil),
/// void (cancel) and refund. Every operation is idempotent in effect — a repeated request never
/// authorizes, captures or refunds the shopper twice.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog lines for the given shopper; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorize (hold) the order total. Scoped to the owning shopper. Returns the updated state.</summary>
    Task<OrderPaymentSummary> PayAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken ct = default);

    /// <summary>Operator action: fulfil the order and capture the money, renewing a stale hold if needed.</summary>
    Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel before fulfilment, releasing the shopper's held funds.</summary>
    Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured order, in full or in part, under a caller-supplied idempotency key. Scoped to the owning shopper.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The shopper's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
