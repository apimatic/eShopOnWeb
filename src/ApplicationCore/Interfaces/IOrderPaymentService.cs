using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and quantity.</summary>
public readonly record struct OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the additive payment flow over the existing order model and the
/// <see cref="IPaymentGateway"/>: place → authorize (hold) → fulfil (capture) / cancel (void) / refund.
/// Enforces shopper scoping and idempotency; persists the PayPal-owned state on <see cref="OrderPayment"/>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper; it starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total for the shopper, by raw card or a saved card. Idempotent.</summary>
    Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId, CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order, capturing the money (renewing a stale authorization first). Idempotent.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the hold. Idempotent.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds the shopper's captured order in full or in part, keyed by a caller idempotency key.</summary>
    Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Returns the caller's orders paired with their payment state.</summary>
    Task<IReadOnlyList<(Order Order, OrderPayment? Payment)>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
