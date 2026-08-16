using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog line requested when placing an order.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public sealed record ShippingAddressInput(
    string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// Owns the payment lifecycle of an API-placed order: place, authorize (hold),
/// fulfil (capture), cancel (void) and refund. Shopper-scoped methods take a
/// buyerId and only ever act on that shopper's own orders; operator methods act
/// on any order.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default);

    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order, capturing the held funds.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
