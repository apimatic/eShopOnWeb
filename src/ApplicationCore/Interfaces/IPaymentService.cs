using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single item on a placed order: a catalog item id and how many.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order (awaiting payment) from catalog items for the given shopper.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat returns the existing hold.</summary>
    Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct = default);

    /// <summary>Fulfils the order: captures the held funds, renewing a stale hold if needed. Operator action.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Cancels before fulfilment: releases the hold so no money moves. Operator action.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a captured payment (full or partial) for the shopper's own order.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
