using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items, Address? ShipTo);

public record PayOrderRequest(CardPaymentDetails? Card, int? PaymentMethodId);

public record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public interface ICheckoutService
{
    string Currency { get; }

    Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(string buyerId, int orderId, RefundOrderRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public record ReconciliationMatch(
    PayPalReportedTransaction PayPal,
    Order? Order,
    string MatchStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> PayPalTransactions,
    IReadOnlyList<Order> EshopOnlyOrders);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
