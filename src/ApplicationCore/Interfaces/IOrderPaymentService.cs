using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a shopper wants to pay: either a raw one-off card, or one of their saved cards.</summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A single line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the payment lifecycle of an order on top of <see cref="IPaymentGateway"/>: placing
/// the order, authorizing, fulfilling (capture, with reauthorization when the hold is stale),
/// cancelling and refunding. Every operation is idempotent in effect. Shopper-scoped operations
/// take the caller's <c>buyerId</c> and act only on that shopper's data.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken);

    /// <summary>Authorizes (holds) the order total. Safe to call twice — the second call is a no-op.</summary>
    Task<Order> AuthorizeAsync(
        string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Operator action: fulfils the order and captures the held funds.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancels the order before fulfilment and releases the hold.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refunds a captured order, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Returns the caller's own orders together with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
}
