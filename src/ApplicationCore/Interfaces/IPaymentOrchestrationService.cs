using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the additive payment capability over the existing order model: places orders, drives the
/// PayPal money movement (authorize → capture → refund/void), and manages saved cards. Enforces shopper
/// ownership, idempotency, and valid status transitions. Every method is separately invocable — no method
/// performs more than the one action it names.
/// </summary>
public interface IPaymentOrchestrationService
{
    Task<PaymentResult<PlacedOrderResult>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineCommand> lines, ShippingAddressCommand? shipTo, CancellationToken ct);

    Task<PaymentResult<OrderPaymentView>> AuthorizeAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct);

    Task<PaymentResult<OrderPaymentView>> FulfilAsync(int orderId, CancellationToken ct);

    Task<PaymentResult<OrderPaymentView>> CancelAsync(int orderId, CancellationToken ct);

    Task<PaymentResult<RefundResultView>> RefundAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    Task<PaymentResult<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    Task<PaymentResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<PaymentResult<SavedCardView>> SaveCardAsync(string buyerId, CardCommand card, CancellationToken ct);

    Task<PaymentResult<IReadOnlyList<SavedCardView>>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    Task<PaymentResult<bool>> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

public sealed record OrderLineCommand(int CatalogItemId, int Quantity);

public sealed record ShippingAddressCommand(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record PlacedOrderResult(int OrderId, string Status, decimal Total, string Currency);

/// <summary>A card to pay with — either a saved card (by id) or one-off card details. Exactly one is used.</summary>
public sealed record PayCommand(int? PaymentMethodId, CardCommand? Card);

public sealed record CardCommand(string? Name, string? Number, string? Expiry, string? SecurityCode, BillingAddressCommand? BillingAddress);

public sealed record BillingAddressCommand(string? AddressLine1, string? AddressLine2, string? AdminArea1, string? AdminArea2, string? PostalCode, string? CountryCode);

public sealed record OrderPaymentView(
    int OrderId,
    string PaymentStatus,
    decimal Amount,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    IReadOnlyList<RefundLineView> Refunds);

public sealed record RefundLineView(int RefundId, string PayPalRefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public sealed record RefundResultView(int RefundId, string PayPalRefundId, decimal Amount, string Status, decimal TotalRefunded, string PaymentStatus);

public sealed record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    OrderPaymentView? Payment);

public sealed record SavedCardView(int PaymentMethodId, string CardBrand, string LastFourDigits, string Expiry, string? CardholderName, DateTimeOffset CreatedAt);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInPayPal);

public sealed record ReconciliationMatch(string TransactionId, int OrderId, decimal? PayPalAmount, decimal EShopAmount, string? TransactionStatus, string PaymentStatus);

public sealed record ReconciliationEntry(string? TransactionId, int? OrderId, decimal? Amount, string? Status, string Note);
