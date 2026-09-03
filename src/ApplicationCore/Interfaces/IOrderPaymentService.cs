using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to order: a catalog item id and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Outcome of authorizing an order's payment (a hold on the funds).</summary>
public record AuthorizationOutcome(
    PaymentStatus PaymentStatus,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string CurrencyCode);

/// <summary>Outcome of capturing at fulfilment, with PayPal's own figures.</summary>
public record CaptureOutcome(
    PaymentStatus PaymentStatus,
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Outcome of a refund.</summary>
public record RefundOutcome(
    string RefundId,
    string Status,
    decimal Amount,
    decimal TotalRefunded,
    PaymentStatus PaymentStatus,
    string CurrencyCode);

/// <summary>
/// Orchestrates the money movement around an order: place, authorize (hold), fulfil (capture),
/// cancel (release) and refund. Ownership scoping (shopper vs operator) is enforced by the caller
/// (the API layer); methods that act on a shopper's own data take a <c>buyerId</c>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for the shopper from catalog items. The order starts awaiting payment.</summary>
    Task<int> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total, paying by one-off card or a saved card. Idempotent in effect.</summary>
    Task<AuthorizationOutcome> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: mark the order fulfilled and capture the money (renewing a stale hold if needed).</summary>
    Task<CaptureOutcome> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancel before fulfilment, releasing any held funds.</summary>
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund the captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, with payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
