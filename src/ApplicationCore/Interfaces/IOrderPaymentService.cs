using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items, Address? ShipToAddress);

public record PayOrderRequest(int OrderId, string BuyerId, CardPaymentSource? Card, int? PaymentMethodId);

public record RefundOrderRequest(int OrderId, string BuyerId, decimal? Amount, string IdempotencyKey);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<Order> PayAsync(PayOrderRequest request, CancellationToken cancellationToken = default);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<(Order Order, OrderRefund Refund)> RefundAsync(RefundOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
    Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public record ReconciliationMatch(
    GatewayTransaction? PayPalTransaction,
    int? OrderId,
    string MatchKind);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount);

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
