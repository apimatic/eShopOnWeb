using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ReconciliationEntry
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Fee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset TransactionTime { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new List<ReconciliationEntry>();
    public List<ReconciliationEntry> UnmatchedPayPalTransactions { get; set; } = new List<ReconciliationEntry>();
    public List<int> OrdersWithoutPayPalTransaction { get; set; } = new List<int>();
}

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default);
    Task<OrderPayment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);
    Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<OrderPayment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
