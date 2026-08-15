using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with its payment state (payment is null while awaiting payment).</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture), cancel
/// (void), refund, plus the shopper's order list and the operator reconciliation report. Payment
/// operations are idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the buyer from catalog items; the order starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total using a one-off card or a saved card.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfils the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancels the order before fulfilment, releasing any held funds.</summary>
    Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order in full or in part; returns the created refund.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The buyer's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>A single order (scoped to the buyer) with its payment state.</summary>
    Task<OrderWithPayment> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: reconciles PayPal's transactions for a range against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
