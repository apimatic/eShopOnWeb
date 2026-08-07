using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates paying for and refunding orders through the payment provider. Ownership checks and
/// payment-state/idempotency rules live here so the HTTP endpoints stay thin.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Pays for the buyer's order with a one-off card. Idempotent: an already-paid order is returned unchanged.</summary>
    Task<Order> PayWithCardAsync(string buyerId, int orderId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Pays for the buyer's order with one of their saved cards. Idempotent for an already-paid order.</summary>
    Task<Order> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Fully refunds the buyer's paid order. Idempotent: an already-refunded order is returned unchanged.</summary>
    Task<Order> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
