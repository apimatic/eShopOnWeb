using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReportedTransaction> PaypalOnly,
    IReadOnlyList<Order> EshopOnly);

public record ReconciliationMatch(Order Order, ReportedTransaction Transaction);

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, PlaceOrderAddress? shipTo, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardPaymentDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
