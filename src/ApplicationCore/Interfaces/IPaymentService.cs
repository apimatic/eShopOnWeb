using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemInput(int CatalogItemId, int Quantity);

public record ReconciliationEntry(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? TransactionTime,
    int? MatchedOrderId,
    int? MatchedPaymentId);

public record UnmatchedPayment(
    int PaymentId,
    int OrderId,
    decimal Amount,
    string Currency,
    IReadOnlyList<string> ProviderTransactionIds);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<UnmatchedPayment> PaymentsWithoutProviderTransaction);

public interface IPaymentService
{
    /// <summary>Creates an order from catalog items at current catalog prices. Starts PendingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemInput> items, Address? shipToAddress, CancellationToken ct);

    /// <summary>
    /// Authorizes (holds) the order total, with either raw card details or one of the buyer's
    /// saved cards. Repeating the call for an already-authorized order returns the existing
    /// payment instead of authorizing again. Returns null when the order does not exist or
    /// belongs to another buyer.
    /// </summary>
    Task<Payment?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct);

    /// <summary>Operator: captures the held funds. Renews a stale authorization first; fails with
    /// PaymentStateException when the hold can no longer be renewed. Idempotent.</summary>
    Task<Payment?> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: releases the hold before fulfilment; no money moves. Idempotent.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Refunds a captured payment, in full (amount null) or in part. The idempotency key is
    /// caller-supplied: repeating the same key returns the original refund; distinct keys are
    /// distinct refunds. Never refunds beyond the captured amount. Returns null when the order
    /// does not exist or belongs to another buyer.
    /// </summary>
    Task<PaymentRefund?> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Operator: lines PayPal's own transaction record up against eShop payments.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
