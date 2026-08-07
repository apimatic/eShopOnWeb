using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and drives their PayPal payment lifecycle (pay, refund).
/// All operations are scoped to the caller so one shopper can never act on another's order.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog item ids + quantities. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays for the order with PayPal, either with one-off card details or a saved card. Idempotent:
    /// a paid order simply returns its existing payment. Returns null if the order is not the caller's.
    /// </summary>
    Task<Order?> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a full refund of the order's payment. Idempotent: a refunded order returns as-is.
    /// Returns null if the order is not the caller's.
    /// </summary>
    Task<Order?> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, with items and payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: exactly one of <see cref="Card"/> (one-off) or <see cref="SavedPaymentMethodId"/>
/// (one of the shopper's saved cards) must be provided.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId);
