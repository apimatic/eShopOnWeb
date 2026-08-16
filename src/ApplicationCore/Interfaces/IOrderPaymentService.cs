using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive "pay for an order" flow: placing an order awaiting payment, authorizing
/// (holding) the total, fulfilling (capturing), cancelling (releasing) and refunding. Each action is
/// separately invocable and idempotent in effect — a double-click never authorizes or captures twice.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>The ISO-4217 currency orders are priced and charged in (from configuration).</summary>
    string Currency { get; }

    /// <summary>Place an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorize the order total by card or by one of the shopper's saved cards. Idempotent: if the
    /// order is already authorized the existing authorization is returned unchanged.
    /// </summary>
    Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund a fulfilled order in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundOutcome> RefundOrderAsync(string callerBuyerId, bool callerIsAdmin, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Load a single order for the caller, enforcing ownership (admins may load any).</summary>
    Task<Order?> GetOrderForCallerAsync(int orderId, string callerBuyerId, bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>All of a shopper's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

public record RefundOutcome(
    string RefundId,
    decimal Amount,
    string Status,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    bool AlreadyProcessed);
