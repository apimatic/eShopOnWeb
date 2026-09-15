using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalSettings
{
    string Currency { get; }
    string ClientId { get; }
    string ClientSecret { get; }
    string Environment { get; }
    string? BaseUrl { get; }
}

public sealed class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipTo, CancellationToken cancellationToken);
    Task<Order> PayAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);
    Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationRow> Matched { get; init; } = Array.Empty<ReconciliationRow>();
    public IReadOnlyList<ProcessorTransaction> PayPalOnly { get; init; } = Array.Empty<ProcessorTransaction>();
    public IReadOnlyList<ReconciliationOrderSummary> EShopOnly { get; init; } = Array.Empty<ReconciliationOrderSummary>();
}

public sealed class ReconciliationRow
{
    public int OrderId { get; init; }
    public ProcessorTransaction Transaction { get; init; } = null!;
}

public sealed class ReconciliationOrderSummary
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
