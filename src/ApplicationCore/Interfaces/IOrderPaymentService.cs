using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

public record CardPaymentRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    ShippingAddressRequest? BillingAddress);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, ShippingAddressRequest? shipTo);

    Task<Order> PayOrderAsync(string buyerId, int orderId, CardPaymentRequest? card, int? paymentMethodId);

    Task<Order> FulfilOrderAsync(int orderId);

    Task<Order> CancelOrderAsync(int orderId);

    Task<OrderRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId);

    Task<Order> GetBuyerOrderAsync(string buyerId, int orderId);
}
