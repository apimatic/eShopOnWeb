using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to order: a catalog item id and a quantity.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay for an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PayOrderInput(PayPalCardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A refund as reflected back to the caller.</summary>
public record RefundView(int Id, string PayPalRefundId, decimal Amount, string Status);

/// <summary>The payment state carried alongside an order, safe to return to a caller.</summary>
public record PaymentView(
    string Currency,
    decimal Amount,
    string PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal? RefundableRemaining,
    string? CardBrand,
    string? CardLast4,
    IReadOnlyList<RefundView> Refunds);

/// <summary>An order with its payment state.</summary>
public record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    OrderStatus Status,
    decimal Total,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment);

public record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>One line of the reconciliation report: PayPal's view lined up against eShop's.</summary>
public record ReconciliationEntry(
    string Category, // "Matched", "InPayPalNotInEShop", "InEShopNotInPayPal"
    string? PayPalTransactionId,
    string? PayPalStatus,
    string? EventCode,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? PayPalDate,
    int? EShopOrderId,
    string? EShopCaptureId,
    OrderStatus? EShopOrderStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalNotInEShopCount,
    int InEShopNotInPayPalCount,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IOrderPaymentService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Shopper-scoped: acts only on the caller's own order.</summary>
    Task<OrderView> AuthorizeAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds.</summary>
    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels an unfulfilled order and releases the held funds.</summary>
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order, in full or in part. Shopper-scoped and idempotent per key.</summary>
    Task<(RefundView refund, OrderView order)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal's transactions against eShop orders for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
