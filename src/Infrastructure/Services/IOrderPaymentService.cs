using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public record OrderItemRequest(int CatalogItemId, int Quantity);

public record PayOrderWithCardRequest(
    string CardNumber,
    int CardExpiryMonth,
    int CardExpiryYear,
    string? Cvv,
    string? CardholderName,
    string BillingCountryCode,
    string? BillingPostalCode);

public record PayOrderResult(
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset AuthorizationExpiry,
    string Currency,
    decimal Amount);

public record FulfilOrderResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount);

public record RefundResult(
    string RefundId,
    string RefundStatus,
    decimal RefundedAmount,
    bool AlreadyExisted);

public record OrderSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    List<RefundSummary> Refunds);

public record RefundSummary(
    string RefundId,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt);

public record ReconciliationEntry(
    string TransactionId,
    string? EShopOrderId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    string? Amount,
    string? Currency,
    string? Fee,
    string? InitiatedDate,
    string? MatchStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    List<ReconciliationEntry> Entries,
    List<string> UnmatchedPayPalTransactions,
    List<string> UnmatchedEShopOrderIds);

public interface IOrderPaymentService
{
    Task<int> CreateOrderAsync(string buyerId, List<OrderItemRequest> items);

    Task<PayOrderResult> PayOrderWithCardAsync(int orderId, string buyerId, PayOrderWithCardRequest card);

    Task<PayOrderResult> PayOrderWithSavedCardAsync(int orderId, string buyerId, int savedCardId);

    Task<FulfilOrderResult> FulfilOrderAsync(int orderId);

    Task CancelOrderAsync(int orderId);

    Task<RefundResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey);

    Task<List<OrderSummary>> GetMyOrdersAsync(string buyerId);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to);
}
