using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(string BuyerId, IReadOnlyList<PlaceOrderItem> Items, Address? ShippingAddress);

public record PayOrderRequest(int OrderId, string BuyerId, CardPaymentDetails? Card, int? PaymentMethodId);

public record RefundOrderRequest(int OrderId, string BuyerId, decimal? Amount, string IdempotencyKey);

public record ReconciliationMatch(
    PayPalReportedTransaction PayPalTransaction,
    int? OrderId,
    string MatchStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<int> EshopOrdersMissingFromPayPal);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(PayOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(RefundOrderRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
