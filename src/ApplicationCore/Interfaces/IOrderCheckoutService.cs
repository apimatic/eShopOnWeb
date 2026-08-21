using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderShipping(string Street, string City, string State, string Country, string ZipCode);

public record RefundOrderResult(Order Order, OrderRefund Refund, bool AlreadyProcessed);

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, PlaceOrderShipping? shipping, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardPaymentSource? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundOrderResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationPaypalOnly> PaypalOnly,
    IReadOnlyList<ReconciliationEshopOnly> EshopOnly);

public record ReconciliationMatch(int OrderId, string PayPalTransactionId, string? Status, decimal? Amount);

public record ReconciliationPaypalOnly(string PayPalTransactionId, string? InvoiceId, string? CustomField, string? Status, decimal? Amount);

public record ReconciliationEshopOnly(int OrderId, string PaymentStatus, string? PayPalCaptureId, string? PayPalAuthorizationId);
