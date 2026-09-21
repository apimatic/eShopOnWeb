using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement flows on top of the existing order model: place, authorize (hold),
/// fulfil (capture), cancel (void), refund, list, reconcile, and saved-card management. Persists the local
/// record before calling PayPal and settles it after; gates each transition on current state so a repeated
/// request never moves money twice.
/// </summary>
public interface IPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipping, CancellationToken ct);

    Task<PaymentActionResult> PayAsync(string buyerId, int orderId, PayInput input, CancellationToken ct);

    /// <summary>Operator action: capture the held funds.</summary>
    Task<PaymentActionResult> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: release the held funds before fulfilment.</summary>
    Task<PaymentActionResult> CancelAsync(int orderId, CancellationToken ct);

    Task<RefundResult> RefundAsync(string buyerId, int orderId, RefundInput input, CancellationToken ct);

    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator action: reconcile PayPal's transaction record against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct);

    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

// ------------------------------ Inputs ------------------------------

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record CardInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddressInput? BillingAddress);

public sealed record BillingAddressInput(
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>Pay with raw card details, or with one of the caller's saved cards. Exactly one must be provided.</summary>
public sealed record PayInput(CardInput? Card, int? SavedPaymentMethodId);

public sealed record RefundInput(decimal? Amount, string IdempotencyKey);

public sealed record SaveCardInput(CardInput Card);

// ------------------------------ Results ------------------------------

public sealed record PlaceOrderResult(int OrderId, decimal Total, string Currency, string PaymentStatus);

public sealed record PaymentActionResult(
    int OrderId,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Amount,
    string Currency,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? Message);

public sealed record RefundResult(
    int OrderId,
    string RefundId,
    decimal Amount,
    string Currency,
    string RefundStatus,
    string PaymentStatus,
    decimal TotalRefunded);

public sealed record MyOrderItemView(int CatalogItemId, string ProductName, int Units, decimal UnitPrice);

public sealed record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    IReadOnlyList<MyOrderItemView> Items);

public sealed record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAtUtc);

public sealed record ReconciliationMatch(int OrderId, string PaymentStatus, string? PayPalTransactionId, decimal? PayPalAmount);

public sealed record ReconciliationLocalOnly(int OrderId, string PaymentStatus, string? PayPalOrderId, string? AuthorizationId, string? CaptureId);

public sealed record ReconciliationPayPalOnly(string? TransactionId, string? ReferenceId, decimal? Amount, string? Currency, DateTimeOffset? InitiatedAtUtc, string? EventCode);

public sealed record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MatchedCount,
    int OnlyInEShopCount,
    int OnlyInPayPalCount,
    bool Truncated,
    int WindowsCovered,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationLocalOnly> OnlyInEShop,
    IReadOnlyList<ReconciliationPayPalOnly> OnlyInPayPal);
