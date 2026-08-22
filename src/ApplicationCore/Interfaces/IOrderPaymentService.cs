using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address shipTo, CancellationToken ct);
    Task<Order> PayAsync(string buyerId, int orderId, int? paymentMethodId, CardPaymentInput? card, CancellationToken ct);
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, CancellationToken ct);
    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct);
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<PayPalTransactionRecord> PayPalOnly,
    IReadOnlyList<ReconciliationOrderSummary> EShopOnly);

public record ReconciliationMatch(int OrderId, PayPalTransactionRecord Transaction);

public record ReconciliationOrderSummary(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total);
