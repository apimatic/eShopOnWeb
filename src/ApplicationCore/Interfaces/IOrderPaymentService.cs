using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(string BuyerId, IReadOnlyList<PlaceOrderItem> Items, Address? ShipTo);

public record PayOrderCommand(string BuyerId, int OrderId, int? PaymentMethodId, CardPaymentInput? Card);

public record RefundOrderCommand(string BuyerId, int OrderId, string IdempotencyKey, decimal? Amount);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken);
    Task<Order> PayAsync(PayOrderCommand request, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderRefund> RefundAsync(RefundOrderCommand request, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
