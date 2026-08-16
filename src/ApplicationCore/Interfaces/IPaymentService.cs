using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to be ordered: a catalog item id and a quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order together with its payment state (used for read models).</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>
/// Orchestrates the money movement for orders: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Idempotent in effect — a repeated request never moves money twice.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items (reusing the existing Order model) awaiting payment.</summary>
    Task<(Order Order, OrderPayment Payment)> PlaceOrderAsync(string buyerId,
        IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total with a raw card or one of the buyer's saved cards.</summary>
    Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentCard? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order — captures the held funds (operator action).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment — voids the hold (operator action).</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, keyed by a caller idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Gets an order's payment by order id, or null.</summary>
    Task<OrderPayment?> GetPaymentByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
