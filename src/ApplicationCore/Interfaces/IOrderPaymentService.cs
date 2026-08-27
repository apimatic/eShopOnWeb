using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items; the order starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or with one of
    /// the shopper's saved cards. Idempotent: paying an already-authorized order returns
    /// the existing payment instead of authorizing again.
    /// </summary>
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: captures the held funds. Renews a stale authorization first;
    /// throws PaymentConflictException in operator-actionable terms when the hold can no
    /// longer be renewed. Idempotent.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds. Idempotent.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: refunds a captured payment in full (amount null) or in part.
    /// Idempotent under the caller-supplied idempotency key.
    /// </summary>
    Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
