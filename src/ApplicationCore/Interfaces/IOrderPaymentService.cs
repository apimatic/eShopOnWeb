using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement lifecycle of an order: place → authorize (hold) →
/// fulfil (capture) → cancel (void) / refund, plus reconciliation against PayPal's records.
/// Every operation is idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the buyer and creates its payment (awaiting payment).</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total using a one-off card or one of the buyer's saved cards.</summary>
    Task<Result<OrderPayment>> PayAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds (renewing a stale hold if needed).</summary>
    Task<Result<OrderPayment>> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels the order before fulfilment, releasing the held funds.</summary>
    Task<Result<OrderPayment>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order in full or in part, keyed for idempotency.</summary>
    Task<Result<PaymentRefund>> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal's transaction records against eShop orders for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested catalog item and quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>How to pay: exactly one of a one-off card or a saved payment method.</summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order paired with its payment (if any).</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>A reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationTransaction> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEShopEntry> InEShopNotInPayPal);

/// <summary>A PayPal transaction matched to an eShop order by merchant reference.</summary>
public record ReconciliationMatch(
    int OrderId,
    string MerchantReference,
    decimal EShopAmount,
    string EShopPaymentStatus,
    ReconciliationTransaction PayPalTransaction);

/// <summary>An eShop payment PayPal's report did not show for the range.</summary>
public record ReconciliationEShopEntry(
    int OrderId,
    string MerchantReference,
    decimal Amount,
    string PaymentStatus,
    DateTimeOffset? CapturedDate);
