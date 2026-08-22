using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken = default);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IPaymentService
{
    Task<OrderPaymentResult> PayAsync(string buyerId, int orderId, CardPaymentRequest? card, int? paymentMethodId, CancellationToken cancellationToken = default);
    Task<OrderPaymentResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderPaymentResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<RefundResult> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record CardPaymentRequest(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    BillingAddressRequest? BillingAddress);

public record BillingAddressRequest(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record OrderPaymentResult(
    int OrderId,
    OrderStatus Status,
    decimal OrderTotal,
    string Currency,
    IReadOnlyList<OrderLineResult> Items,
    PaymentStateResult Payment);

public record OrderLineResult(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public record PaymentStateResult(
    string? PayPalOrderId,
    string? PayPalOrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiration,
    decimal? AuthorizedAmount,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    IReadOnlyList<RefundStateResult> Refunds);

public record RefundStateResult(string RefundId, string Status, decimal Amount, string Currency, string IdempotencyKey);

public record RefundResult(string RefundId, int OrderId, OrderStatus OrderStatus, decimal Amount, string Currency, decimal RemainingRefundable, string Status);

public record ShopperOrderResult(
    int OrderId,
    OrderStatus Status,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineResult> Items,
    PaymentStateResult? Payment);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> PayPalOnly,
    IReadOnlyList<ReconciliationRow> EshopOnly);

public record ReconciliationRow(
    string? PayPalTransactionId,
    string? PayPalInvoiceId,
    string? PayPalCustomField,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? PayPalCurrency,
    DateTimeOffset? PayPalTime,
    int? OrderId,
    OrderStatus? OrderStatus,
    string? CaptureId,
    string? AuthorizationId,
    string MatchReason);
