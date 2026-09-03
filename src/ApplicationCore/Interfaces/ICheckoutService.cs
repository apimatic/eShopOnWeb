using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderCommand(string BuyerId, IReadOnlyList<PlaceOrderItem> Items, Address ShipTo);

public record PayOrderCommand(
    int OrderId,
    string BuyerId,
    CardPaymentDetails? Card,
    int? PaymentMethodId);

public record RefundOrderCommand(int OrderId, string BuyerId, decimal? Amount, string IdempotencyKey);

public record ReconciliationRow(
    string Kind,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalInvoiceId,
    string? MatchStatus,
    string? Amount,
    string? Status);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Rows,
    int PayPalTransactionCount,
    int EshopOrderCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount);

public record RefundOutcome(Order Order, OrderRefund Refund);
public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken cancellationToken);
    Task<Order> PayAsync(PayOrderCommand command, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundOutcome> RefundAsync(RefundOrderCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
