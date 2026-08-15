using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item requested when placing an order: a catalog item id and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>How to pay for an order: either raw card details, or a saved card id — exactly one.</summary>
public record PaymentInstrument(CardDetails? Card, int? SavedCardId);

/// <summary>
/// Orchestrates the full order + payment lifecycle: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each action is separately invocable.
/// </summary>
public interface IPaymentProcessingService
{
    /// <summary>Places an order for the buyer from catalog items. Prices come from the catalog, not the caller.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat never places a second hold.</summary>
    Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order, capturing the held funds. Renews a stale hold if needed. Idempotent.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds. Idempotent.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order in full or in part. The idempotency key prevents double refunds.</summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads a single order for the buyer (their own only), including payment state.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
