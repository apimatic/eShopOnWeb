using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and how many of it to order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the additive pay-for-an-order flow on top of the existing order model:
/// place, authorize (hold), fulfil (capture), cancel (void), refund, and read.
/// Payment operations are idempotent in effect — a double-click never charges twice.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the caller. Order starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total with a one-off card or one of the caller's saved cards.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and takes the money, renewing a stale hold if needed.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the caller's captured order, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
