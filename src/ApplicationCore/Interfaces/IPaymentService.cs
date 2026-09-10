using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-payment lifecycle: place, authorize (hold), fulfil (capture), cancel (void),
/// refund, plus the shopper's order list and the operator reconciliation report. Enforces ownership,
/// per-order serialization and idempotency; drives PayPal through <see cref="IPayPalGateway"/>.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total. Idempotent: a repeat never authorizes twice.</summary>
    Task<PaymentView> AuthorizeAsync(int orderId, string buyerId, PayInput pay, CancellationToken ct);

    /// <summary>Operator action: capture the held funds; renews a stale authorization first if needed.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: void the hold before capture, releasing the shopper's funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the caller's own captured order, in full or in part, keyed for idempotency.</summary>
    Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator action: PayPal's transactions for a range lined up against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>Pay input: either a one-off card or a saved-card id. Exactly one must be supplied.</summary>
public record PayInput(CardInput? Card, int? SavedCardId);

public record CardInput(string Number, string Expiry, string SecurityCode, string? Name,
    string? CountryCode, string? AddressLine1, string? AddressLine2, string? AdminArea1, string? AdminArea2,
    string? PostalCode);

public record PaymentView(
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    string? InvoiceId,
    string? InstrumentDescription,
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
    decimal RefundableRemaining,
    IReadOnlyList<RefundLine> Refunds);

public record RefundLine(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public record RefundView(string RefundId, int OrderId, decimal Amount, string Status,
    decimal RefundedTotal, decimal RefundableRemaining);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationTransaction> PayPalOnly,
    IReadOnlyList<ReconciliationOrder> EShopOnly,
    int PayPalTransactionCount,
    int EShopCapturedCount);

public record ReconciliationTransaction(string? TransactionId, string? InvoiceId, decimal? Amount,
    string? Currency, string? Status, DateTimeOffset? Date, decimal? Fee);

public record ReconciliationOrder(int OrderId, string? InvoiceId, string? CaptureId, decimal? CapturedAmount,
    string Currency, string Status);

public record ReconciliationMatch(int OrderId, ReconciliationTransaction PayPalTransaction,
    decimal? EShopCapturedAmount, bool AmountsAgree);
