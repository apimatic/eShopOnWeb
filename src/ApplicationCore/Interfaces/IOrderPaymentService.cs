using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement lifecycle of an order: place → authorize (hold) → fulfil (capture)
/// or cancel (void) → refund. Each step is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the buyer from catalog items. Amounts come from catalog prices. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a re-issued pay never authorizes twice. Shopper-scoped.</summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds, renewing a stale authorization if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the hold so no money moved.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order, full or partial, under a caller-supplied idempotency key. Shopper-scoped.</summary>
    Task<(Order Order, Refund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
