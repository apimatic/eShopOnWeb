using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money flow that follows an order: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for a shopper and open its payment in a pending state.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Authorize the order total for the shopper — a hold, not a capture — using either raw card details
    /// or one of the shopper's saved cards. Idempotent: a repeat while already authorized is a no-op.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: fulfil the order, capturing the held funds. A stale authorization is renewed
    /// first; one that can no longer be renewed fails with an operator-actionable message.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refund a captured order for the shopper, in full or in part. The idempotency key makes a repeated
    /// request a no-op; two distinct keys are two legitimate partial refunds.
    /// </summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);
}
