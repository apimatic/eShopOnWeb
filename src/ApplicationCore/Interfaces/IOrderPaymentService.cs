using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund, bool AlreadyExisted)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
