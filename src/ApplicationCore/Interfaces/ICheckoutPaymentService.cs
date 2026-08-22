using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string? LastRefreshedDatetime,
    IReadOnlyList<PayPalReportedTransaction> PayPalTransactions,
    IReadOnlyList<Order> EShopOrdersInRange,
    IReadOnlyList<PayPalReportedTransaction> UnmatchedPayPal,
    IReadOnlyList<Order> UnmatchedEShop);
