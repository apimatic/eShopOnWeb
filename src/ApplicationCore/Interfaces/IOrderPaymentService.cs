using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item and quantity to place on an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>How to pay: either raw card details for a one-off payment, or a saved card by id. Exactly one is set.</summary>
public record PayInstruction(CardDetails? Card, int? PaymentMethodId);

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing Order/OrderItem model: place, authorize
/// (hold), fulfil (capture), cancel (void), refund. Shopper-scoped operations take the caller's buyerId and
/// act only on that caller's data; operator operations (fulfil, cancel) do not.
/// </summary>
public interface IOrderPaymentService
{
    Task<Payment> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken ct = default);

    Task<Payment> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken ct = default);

    /// <summary>Operator action: capture the held funds at fulfilment.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: void the hold before fulfilment.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a captured payment (full or partial) and returns the updated payment. The created (or, on a
    /// repeated idempotency key, the existing) refund can be found on the payment by that key.
    /// </summary>
    Task<Payment> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
