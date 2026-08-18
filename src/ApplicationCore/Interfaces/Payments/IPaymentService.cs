using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Orchestrates the money lifecycle on top of <see cref="IPayPalGateway"/> and the order/saved-card
/// repositories. Every shopper-scoped method takes the caller's <c>buyerId</c> and acts only on that
/// shopper's data. Operations are idempotent in effect: a double-click never authorizes or captures twice.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items (priced from the catalog); returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, CancellationToken ct);

    /// <summary>Authorize (hold) the order total using card details or a saved card. Idempotent per order.</summary>
    Task PayOrderAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct);

    /// <summary>Operator action: fulfil the order and capture the money, renewing a stale hold if needed.</summary>
    Task FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Refund the captured payment in full or in part; returns the new refund's id. Idempotent per key.</summary>
    Task<int> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator action: reconcile PayPal's transaction records against eShop orders over a date range.</summary>
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Save (vault) a card for the shopper; returns the saved card's id.</summary>
    Task<int> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethodSummary>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove a saved card; afterwards it is neither listed nor usable to pay.</summary>
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
