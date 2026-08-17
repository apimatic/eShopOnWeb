using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single catalog line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

/// <summary>An order paired with its payment state, for the shopper's own view.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Drives Flow 1 — placing an order, holding the money (authorize), taking it (fulfil/capture),
/// releasing it (cancel/void), and returning it (refund) — reusing the existing order model.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shippingAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total (holds the money) using a one-off card or one of the shopper's
    /// saved cards. Idempotent in effect: a double-click never authorizes twice.
    /// </summary>
    Task<Payment> AuthorizeAsync(
        string buyerId,
        int orderId,
        PayPalCardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: fulfils the order and captures the money, renewing a stale hold if needed.
    /// Idempotent in effect: repeated calls never capture twice.
    /// </summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured order in full or in part. Scoped to the order's owner unless the caller
    /// is an operator. Idempotent under <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<PaymentRefund> RefundAsync(
        int orderId,
        string requesterBuyerId,
        bool requesterIsAdmin,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each with its payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
