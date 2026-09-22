using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the payment flows over the eShop order model, the local payment records, and the PayPal
/// gateway. Each action is separately invocable; none does more than its own step. Shopper-scoped actions
/// take a <c>buyerId</c> and act only on that caller's data; operator actions act across owners.
/// </summary>
public interface IPaymentOrchestrationService
{
    /// <summary>Place an order from catalog items for the caller; the payment starts awaiting authorization.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct);

    /// <summary>Authorize (hold) the order total with a one-off card or one of the caller's saved cards.</summary>
    Task<PaymentView> PayAsync(string buyerId, int orderId, PayInput input, CancellationToken ct);

    /// <summary>Operator: fulfil the order — capture the held funds, renewing a stale authorization first.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment — release the held funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured payment (full or partial) for the caller's own order, idempotently.</summary>
    Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: reconcile PayPal's transaction record against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

// --- inputs ---

public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>A one-off card OR a saved-card id. Exactly one must be supplied.</summary>
public record PayInput
{
    public int? SavedPaymentMethodId { get; init; }
    public CardInput? Card { get; init; }
}

public record CardInput
{
    public required string Number { get; init; }
    /// <summary>Expiry in YYYY-MM.</summary>
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public string? BillingAddressLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
}

// --- results ---

public record PlaceOrderResult
{
    public required int OrderId { get; init; }
    public required decimal Total { get; init; }
    public required string CurrencyCode { get; init; }
}

public record RefundView
{
    public required string? RefundId { get; init; }
    public required decimal Amount { get; init; }
    public required string? Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public record PaymentView
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedGross { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal TotalRefunded { get; init; }
    public decimal RemainingRefundable { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<RefundView> Refunds { get; init; } = Array.Empty<RefundView>();
}

public record RefundResult
{
    public required string RefundId { get; init; }
    public required decimal Amount { get; init; }
    public required string? Status { get; init; }
    public required PaymentView Payment { get; init; }
}

// --- reconciliation ---

public enum ReconciliationMatch
{
    /// <summary>PayPal and eShop both know the transaction.</summary>
    Matched = 0,
    /// <summary>PayPal reports it; eShop has no matching order.</summary>
    PayPalOnly = 1,
    /// <summary>eShop has a payment PayPal's report does not (yet) show.</summary>
    EShopOnly = 2
}

public record ReconciliationLine
{
    public required ReconciliationMatch Match { get; init; }
    public string? InvoiceReference { get; init; }
    public int? OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? EShopAmount { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? TransactionDate { get; init; }
}

public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    /// <summary>True when the whole range was walked without hitting a protective page cap.</summary>
    public required bool Complete { get; init; }
    public required int WindowsScanned { get; init; }
    public required int PagesScanned { get; init; }
    public required int MatchedCount { get; init; }
    public required int PayPalOnlyCount { get; init; }
    public required int EShopOnlyCount { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
}
