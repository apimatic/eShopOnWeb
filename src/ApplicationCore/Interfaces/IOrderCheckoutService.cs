using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipToAddress);
    Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId);
    Task<Order> GetOrderForOperatorAsync(int orderId);
    Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId);
    Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentSource card);
    Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId);
    Task<Order> FulfilAsync(int orderId);
    Task<Order> CancelAsync(int orderId);
    Task<OrderRefund> RefundAsync(int orderId, string actorBuyerId, bool actorIsAdmin, decimal? amount, string idempotencyKey);
}
