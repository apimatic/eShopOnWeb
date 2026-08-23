using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CreatePaidOrderItem(int CatalogItemId, int Quantity);

public record ShipToAddressDto(string Street, string City, string State, string Country, string ZipCode);

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<CreatePaidOrderItem> items, ShipToAddressDto? shipTo, CancellationToken cancellationToken = default);
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<Order> GetShopperOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListShopperOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReportedTransaction> PayPalOnly,
    IReadOnlyList<ReconciliationLocalPayment> LocalOnly);

public record ReconciliationMatch(int OrderId, string PayPalTransactionId, string? Status, decimal? Amount);
public record ReconciliationLocalPayment(int OrderId, string Status, string? PayPalOrderId, string? AuthorizationId, string? CaptureId);
