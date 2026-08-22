using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken ct);
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, string? paymentMethodId, CancellationToken ct);
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
