using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<ShopperOrder> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderPayment> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrder>> ListMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed record ShopperOrder(Order Order, OrderPayment? Payment);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<ShopperOrder> EshopOnly);

public sealed record ReconciliationMatch(ShopperOrder Order, PayPalReportedTransaction Transaction);
