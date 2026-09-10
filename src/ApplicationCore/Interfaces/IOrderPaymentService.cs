using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How the shopper wants to pay: with raw card details, or with a saved card.</summary>
public abstract record PaymentInstrument;
public sealed record CardPaymentInstrument(CardDetails Card) : PaymentInstrument;
public sealed record SavedCardPaymentInstrument(int PaymentMethodId) : PaymentInstrument;

/// <summary>
/// Orchestrates the order/payment lifecycle: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund, keeping the local <see cref="Order"/> aggregate and PayPal in
/// step. All mutating operations are idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: repeating it never places a
    /// second hold. The order must belong to <paramref name="buyerId"/>.</summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstrument instrument, CancellationToken ct = default);

    /// <summary>Operator action: fulfil the order and capture the money. Renews a stale
    /// authorization first; throws <see cref="Exceptions.PaymentException"/> if it can no
    /// longer be renewed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a fulfilled order in full (amount null) or in part. The
    /// <paramref name="idempotencyKey"/> dedupes retries. The order must belong to
    /// <paramref name="buyerId"/>.</summary>
    Task<(Order Order, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount, CancellationToken ct = default);

    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);
}
