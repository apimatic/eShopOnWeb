using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-payment lifecycle: place, authorize (pay), fulfil (capture), cancel (void)
/// and refund. Each operation is idempotent in effect and enforces shopper ownership at the service
/// boundary via the supplied buyer id.
/// </summary>
public interface IPaymentProcessingService
{
    /// <summary>Places an order for the buyer from catalog items, priced from the catalog. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, ShippingAddress? shipTo, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. The order must belong to <paramref name="buyerId"/>.</summary>
    Task<Order> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order: captures the held funds (operator action). Renews a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment: releases the hold (operator action).</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundOutcome> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns a single order for the buyer, or null if it does not exist or is not theirs.</summary>
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>A ship-to address supplied when placing an order.</summary>
public record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay: either raw <see cref="Card"/> details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be provided.
/// </summary>
public record PayInstruction(PayPalCard? Card, int? SavedPaymentMethodId);

/// <summary>The result of a refund: the PayPal refund id plus the updated order.</summary>
public record RefundOutcome(string RefundId, Order Order);
