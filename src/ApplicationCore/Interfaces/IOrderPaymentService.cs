using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address shipTo, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(string buyerId, int orderId, RefundOrderRequest request, bool isAdministrator, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PayOrderRequest
{
    public int? PaymentMethodId { get; init; }
    public CardPaymentRequest? Card { get; init; }
}

public sealed class CardPaymentRequest
{
    public required string Name { get; init; }
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required BillingAddressRequest BillingAddress { get; init; }
}

public sealed class BillingAddressRequest
{
    public required string AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public required string AdminArea2 { get; init; }
    public required string AdminArea1 { get; init; }
    public required string PostalCode { get; init; }
    public required string CountryCode { get; init; }
}

public sealed class RefundOrderRequest
{
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}
