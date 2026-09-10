using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested catalog line for a new order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Combined order + payment-state projection for the caller's orders.</summary>
public record OrderPaymentSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedToDate,
    int? SavedPaymentMethodId);

/// <summary>One reconciliation row lining a PayPal transaction up against an eShop order.</summary>
public record ReconciliationLine(
    string MatchStatus,             // Matched | InPayPalOnly | InEShopOnly
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? InvoiceId,
    int? OrderId,
    string? EShopPaymentStatus,
    decimal? EShopCapturedGross);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>Places an order from catalog items, reusing the existing Order/OrderItem model.</summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken cancellationToken = default);
}

/// <summary>Drives the money lifecycle for an order: authorize, fulfil (capture), cancel, refund.</summary>
public interface IOrderPaymentService
{
    Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentCard? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentSummary>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Saves, lists and removes a shopper's vaulted cards.</summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PaymentCard card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>Builds the PayPal-vs-eShop reconciliation report for a date range.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
