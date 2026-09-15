using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PlaceOrderRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public IReadOnlyList<PlaceOrderItem> Items { get; init; } = [];
    public Address? ShipTo { get; init; }
}

public sealed class PayOrderRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public int? PaymentMethodId { get; init; }
    public CardPaymentDetails? Card { get; init; }
}

public sealed class FulfilOrderRequest
{
    public int OrderId { get; init; }
}

public sealed class CancelOrderRequest
{
    public int OrderId { get; init; }
}

public sealed class RefundOrderRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
}

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> PayOrderAsync(PayOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(FulfilOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(RefundOrderRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
