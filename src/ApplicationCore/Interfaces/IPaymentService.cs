using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and how many of it to order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture),
/// cancel (release), refund (return), plus the caller's order list and operator reconciliation.
/// Shopper-scoped methods take the caller's buyerId and act only on their own orders.
/// </summary>
public interface IPaymentService
{
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    Task<Result<Order>> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: capture the held funds and mark the order fulfilled.</summary>
    Task<Result<Order>> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: release held funds before fulfilment and cancel the order.</summary>
    Task<Result<Order>> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<PaymentRefund>> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Order>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: PayPal's transactions for a range, lined up against eShop orders.</summary>
    Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
