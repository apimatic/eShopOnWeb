using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive payment/fulfilment flows over the existing order model and the PayPal
/// gateway. Provider-agnostic: it speaks only <see cref="IPayPalPaymentGateway"/> and the domain.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper; it starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? shipping, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total with a one-off card or a saved card. Idempotent.</summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct);

    /// <summary>Operator: fulfils the order and captures (takes) the held funds. Renews a stale hold.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment, releasing the held funds. No money moves.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the captured payment in full or in part under a caller idempotency key.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Saves (vaults) a card for the shopper; returns the new saved-card id.</summary>
    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes one of the caller's saved cards; it can no longer be used to pay.</summary>
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Operator: reconciles PayPal's transaction records against eShop orders over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public record OrderLine(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Pay with raw card details, or with one of the shopper's saved cards (exactly one).</summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

public record RefundOutcome(PaymentRefund Refund, OrderPayment Payment);

public record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    OrderPaymentStatus Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? CardBrand,
    string? CardLast4,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundView> Refunds);

public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInPayPal);

public record ReconciliationEntry(
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalOrderId,
    string? CaptureId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Date);
