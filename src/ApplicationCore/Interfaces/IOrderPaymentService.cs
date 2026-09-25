using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item requested when placing an order via the API.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing <see cref="Order"/> model and the PayPal
/// gateway. Every method that acts on an order is given the caller's <c>buyerId</c> and acts only on
/// that shopper's order, except the operator actions which are gated by role at the endpoint.
/// </summary>
public interface IOrderPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorize (hold) the order total, funded by a raw card or one of the shopper's saved cards.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardInput? card, string? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator: fulfil the order and capture (take) the held funds.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured payment (full or partial) under a caller-supplied idempotency key.</summary>
    Task<(OrderPaymentView Payment, RefundView Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct = default);

    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<OrderPaymentView?> GetOrderAsync(string buyerId, int orderId, CancellationToken ct = default);
}
