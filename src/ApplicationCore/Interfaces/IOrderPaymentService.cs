using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardPaymentInput? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record OrderLine(int CatalogItemId, int Quantity);

public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<PaypalReportedTransaction> PaypalTransactions { get; init; } = Array.Empty<PaypalReportedTransaction>();
    public IReadOnlyList<ReconciliationMatch> Matches { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PaypalReportedTransaction> PaypalOnly { get; init; } = Array.Empty<PaypalReportedTransaction>();
    public IReadOnlyList<EshopPaymentRecord> EshopOnly { get; init; } = Array.Empty<EshopPaymentRecord>();
}

public class ReconciliationMatch
{
    public PaypalReportedTransaction Paypal { get; init; } = default!;
    public EshopPaymentRecord Eshop { get; init; } = default!;
}

public class EshopPaymentRecord
{
    public int OrderId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string PaypalId { get; init; } = string.Empty;
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
}
