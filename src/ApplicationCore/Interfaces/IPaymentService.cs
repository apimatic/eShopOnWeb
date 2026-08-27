using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices. Starts PendingPayment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total with PayPal, using either one-off card details or one of the
    /// shopper's saved cards. Repeating the call for an already-authorized order returns the existing
    /// authorization instead of charging again.
    /// </summary>
    Task<Payment> PayOrderAsync(int orderId, string buyerId, PayPalCardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: captures the held funds, renewing the authorization first if it has gone stale.
    /// Repeating the call for an already-captured order returns the existing capture.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: refunds a captured payment in full (amount omitted) or in part. The idempotency key
    /// guarantees a repeated request under the same key never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders together with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card with PayPal and stores only safe display data locally.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card both locally and from PayPal's vault.</summary>
    Task DeleteSavedCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: lines PayPal's own record of transactions over the range up against eShop payments,
    /// covering every page of the range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

public sealed record OrderWithPayment(Order Order, Payment? Payment);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<ReconciliationUnmatchedPayment> PaymentsMissingFromPayPal);

public sealed record ReconciliationEntry(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    int? MatchedOrderId,
    string MatchState); // "Matched" or "PayPalOnly"

public sealed record ReconciliationUnmatchedPayment(
    int OrderId,
    int PaymentId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId);
