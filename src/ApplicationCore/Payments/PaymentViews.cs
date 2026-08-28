using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The payment state of one order, as an API caller sees it.</summary>
public sealed record PaymentView(
    int OrderId,
    string PaymentStatus,
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
    decimal TotalRefunded,
    decimal RefundableRemaining,
    bool AwaitingReconciliation,
    IReadOnlyList<RefundView> Refunds)
{
    public static PaymentView From(Payment payment) => new(
        payment.OrderId,
        payment.Status.ToString(),
        payment.Amount,
        payment.CurrencyCode,
        payment.PayPalOrderId,
        payment.AuthorizationId,
        payment.AuthorizationStatus,
        payment.AuthorizationExpiresAt,
        payment.CaptureId,
        payment.CaptureStatus,
        payment.CapturedAmount,
        payment.PayPalFee,
        payment.NetAmount,
        payment.TotalRefunded,
        payment.RefundableRemaining,
        payment.AwaitingReconciliation,
        payment.Refunds.Select(RefundView.From).ToList());
}

public sealed record RefundView(
    int RefundRecordId,
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset CreatedAt)
{
    public static RefundView From(PaymentRefund refund) => new(
        refund.Id, refund.PayPalRefundId, refund.Status, refund.Amount, refund.CurrencyCode, refund.CreatedAt);
}

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    string CurrencyCode,
    IReadOnlyList<OrderItemView> Items,
    PaymentView? Payment)
{
    public static OrderView From(Order order, Payment? payment, string currencyCode) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.Total(),
        currencyCode,
        order.OrderItems
            .Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList(),
        payment is null ? null : PaymentView.From(payment));
}

public sealed record SavedCardView(
    int PaymentMethodId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt)
{
    public static SavedCardView From(SavedCard card) => new(
        card.Id, card.Brand, card.LastDigits, card.Expiry, card.CardholderName, card.CreatedAt);
}

/// <summary>A PayPal transaction that was successfully lined up against an eShop payment.</summary>
public sealed record ReconciliationMatch(
    string TransactionId,
    string MatchedOn,
    int OrderId,
    int PaymentId,
    string PaymentStatus,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    bool AmountsAgree,
    string? TransactionStatus,
    DateTimeOffset? InitiatedAt);

/// <summary>An eShop payment with money movement that PayPal's reporting does not show for the range.</summary>
public sealed record ReconciliationUnmatchedPayment(
    int OrderId,
    int PaymentId,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Amount,
    decimal? CapturedAmount,
    string CurrencyCode,
    bool AwaitingReconciliation,
    string Note);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    DateTimeOffset? ProviderLastRefreshedAt,
    int ProviderTransactionCount,
    int MatchedCount,
    int OnlyAtPayPalCount,
    int OnlyInEShopCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<GatewayTransaction> OnlyAtPayPal,
    IReadOnlyList<ReconciliationUnmatchedPayment> OnlyInEShop);
