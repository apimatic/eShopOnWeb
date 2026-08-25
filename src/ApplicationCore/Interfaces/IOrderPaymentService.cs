using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> AuthorizePaymentAsync(int orderId, string buyerId, PayPalCardDetails? card, int? paymentMethodId, CancellationToken ct = default);
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default);
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);
    Task<(Order Order, OrderPaymentRefund Refund)> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);
    Task<ReconciliationReport> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
