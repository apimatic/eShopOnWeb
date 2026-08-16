using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of a placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How a shopper wants to pay: either raw card details for a one-off, or one of their saved cards.</summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order paired with its payment state, for listing.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the money movement for an order: place, authorize (hold), fulfil (capture),
/// cancel (release) and refund. All shopper-scoped operations act only on the caller's own order.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken = default);

    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<OrderWithPayment?> GetOrderForBuyerAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default);
}
