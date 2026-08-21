using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId);
    Task<Order> FulfilAsync(int orderId);
    Task<Order> CancelAsync(int orderId);
    Task<PaymentRefund> RefundAsync(int orderId, string callerBuyerId, bool isAdministrator, string idempotencyKey, decimal? amount);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId);
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);
    Task DeleteAsync(string buyerId, int paymentMethodId);
}

public record ReconciliationRow(
    string? EshopOrderId,
    string? PayPalTransactionId,
    string MatchStatus,
    string? EshopPaymentState,
    string? PayPalStatus,
    string? Amount,
    string? Currency,
    DateTimeOffset? OccurredAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Rows,
    int PayPalTransactionCount,
    int EshopPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EshopOnlyCount);

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
