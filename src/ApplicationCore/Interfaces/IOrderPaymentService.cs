using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement over the lifetime of an order: place, authorize (pay),
/// fulfil (capture), cancel (void) and refund, plus reading the caller's orders with payment state.
/// Shopper-scoped operations take the caller's <c>buyerId</c> and act only on that shopper's data.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>Authorizes the order total (holds the money) using a card or a saved card. Idempotent in effect.</summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale authorization if needed.</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels the order before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order in full or in part. Idempotent on <paramref name="idempotencyKey"/>.</summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
