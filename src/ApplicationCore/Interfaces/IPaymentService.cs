using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line of an order to place: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to fund a payment: raw card details for a one-off charge, or the id of one of the
/// caller's saved cards. Exactly one must be provided.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order paired with its payment state (null when still awaiting payment).</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing order model and the PayPal
/// gateway.
/// </summary>
public interface IPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total. Idempotent: repeating never holds twice.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken ct);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale hold if needed.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment in full or in part, idempotent under the supplied key.</summary>
    Task<(Payment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator action: reconciles PayPal transactions in a range against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
