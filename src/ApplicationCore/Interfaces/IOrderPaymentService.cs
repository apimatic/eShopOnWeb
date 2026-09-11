using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line requested when placing an order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How a shopper wants to pay: either raw <paramref name="Card"/> details for a one-off payment,
/// or a saved card identified by <paramref name="SavedPaymentMethodId"/>. Exactly one must be set.
/// </summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order paired with its payment state, for read scenarios.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the money movement and operator flows around an order: place, authorize (hold),
/// fulfil (capture), cancel (release) and refund. Each action is separately invocable and
/// idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for a shopper from catalog items. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total. Shopper-scoped.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Fulfil the order — capture (take) the money. Operator action.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel before fulfilment — release the held funds. Operator action.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a fulfilled order, in full or in part. Shopper-scoped, idempotent by key.</summary>
    Task<(Refund Refund, Payment Payment)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);
}
