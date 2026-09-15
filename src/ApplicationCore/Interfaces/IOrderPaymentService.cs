using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the money movement for an order: place (awaiting payment), authorize (hold), fulfil
/// (capture), cancel (void), and refund. Shopper-scoped operations take the caller's buyer id and
/// act only on that shopper's own orders.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total: places a hold on the money without taking it. Idempotent — a
    /// second call once authorized is a no-op. Pays with the card or saved card in the instrument.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: marks the order fulfilled and captures the money. Renews a stale
    /// authorization rather than failing; reports when it can no longer be renewed. Idempotent.
    /// </summary>
    Task<FulfilmentResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds. Idempotent.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a fulfilled order, fully or partially. The idempotency key makes repeats safe; two
    /// distinct partial refunds use distinct keys. Returns the new refund's id.
    /// </summary>
    Task<int> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
