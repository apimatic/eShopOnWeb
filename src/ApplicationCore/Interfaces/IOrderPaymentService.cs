using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing order model and <see cref="IPayPalGateway"/>.
/// Shopper-scoped operations (<see cref="PayAsync"/>, <see cref="RefundAsync"/>, <see cref="GetMyOrdersAsync"/>)
/// act only on the caller's own orders; <see cref="FulfilAsync"/> and <see cref="CancelAsync"/> are operator actions.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for the buyer; it starts awaiting payment. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipping, CancellationToken ct);

    /// <summary>Authorize (hold) the order total using a one-off card or a saved card. Idempotent under double-submit.</summary>
    Task<OrderPaymentSummary> PayAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct);

    /// <summary>Operator: fulfil the order — capture the held funds (renewing a stale authorization first if needed).</summary>
    Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel the order before fulfilment — release the held funds.</summary>
    Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund the caller's captured order, full or partial, under a caller-supplied idempotency key.</summary>
    Task<RefundResponse> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
}
