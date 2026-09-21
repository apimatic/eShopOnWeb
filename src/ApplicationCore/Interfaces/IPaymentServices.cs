using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address for a placed order; a placeholder is used when omitted.</summary>
public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

/// <summary>The money/fulfilment state of an order, safe to return to a caller.</summary>
public record OrderPaymentView(
    int OrderId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    IReadOnlyList<RefundView> Refunds);

public record RefundView(int Id, string? PayPalRefundId, decimal Amount, string Status);

public record MyOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    OrderPaymentView? Payment,
    IReadOnlyList<MyOrderItemView> Items);

public record SavedCardView(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, DateTimeOffset CreatedAt);

public record ReconciliationMatch(
    string? InvoiceId,
    int? OrderId,
    string? LocalStatus,
    decimal? LocalAmount,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int PagesFetched,
    bool Truncated,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationMatch> InPayPalNotEshop,
    IReadOnlyList<ReconciliationMatch> InEshopNotPayPal);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize, fulfil (capture), cancel (void),
/// refund, and query. Shopper-scoped operations act only on the caller's own orders; fulfil and
/// cancel are operator actions.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for the given buyer; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct);

    /// <summary>Authorize (hold) the order total using a one-off card or one of the buyer's saved cards.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct);

    /// <summary>Operator: capture the held funds at fulfilment, renewing a stale authorization first.</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: void the hold before fulfilment so no money moves.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund the caller's captured payment, in full or in part, under an idempotency key.</summary>
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's own orders, each with its payment state.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
}

/// <summary>Saving, listing and removing a shopper's reusable cards.</summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct);
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

/// <summary>Operator report lining PayPal's transactions up against eShop orders over a date range.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
