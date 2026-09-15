using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog line requested when placing an order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>How a shopper chose to pay: a one-off card, or one of their saved cards.</summary>
public abstract record PayInstruction;

public sealed record PayWithCardInstruction(PayPalCardDetails Card) : PayInstruction;

public sealed record PayWithSavedCardInstruction(int PaymentMethodId) : PayInstruction;

/// <summary>
/// Orchestrates the money movement around an order: placing it, holding the funds (authorize),
/// taking them (capture at fulfilment), releasing them (cancel) and returning them (refund).
/// Every shopper-scoped method enforces that the caller owns the order.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for a buyer. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat never authorizes twice.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds at fulfilment, renewing a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order, fully or partially, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>A single order scoped to its owner (null if not found or not the caller's).</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
