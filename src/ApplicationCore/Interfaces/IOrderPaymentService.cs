using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line requested when placing an order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: either raw <see cref="Card"/> details for a one-off payment, or one of
/// the shopper's <see cref="SavedPaymentMethodId"/> saved cards. Exactly one must be supplied.
/// </summary>
public class PaymentInstruction
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>
/// Orchestrates the pay / fulfil / cancel / refund lifecycle of an order against the payment gateway.
/// Shopper-scoped calls take the caller's <c>buyerId</c> and act only on their own orders; operator
/// calls (<see cref="FulfilAsync"/>, <see cref="CancelAsync"/>, <see cref="ReconcileAsync"/>) do not.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
