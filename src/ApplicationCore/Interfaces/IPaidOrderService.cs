using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaidOrderService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress);
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId);
    Task<Order> FulfilAsync(int orderId);
    Task<Order> CancelAsync(int orderId);
    Task<Order> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId);
    Task<ReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed class ReconciliationReport
{
    public System.DateTimeOffset From { get; init; }
    public System.DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = System.Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = System.Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<Order> EshopOnly { get; init; } = System.Array.Empty<Order>();
}

public sealed class ReconciliationMatch
{
    public Order Order { get; init; } = null!;
    public PayPalReportedTransaction Transaction { get; init; } = null!;
}
