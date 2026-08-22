using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CreatePaidOrderItem(int CatalogItemId, int Quantity);

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CreatePaidOrderItem> items, Address? shipTo, CancellationToken ct);
    Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentInput card, CancellationToken ct);
    Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken ct);
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken ct);
}
