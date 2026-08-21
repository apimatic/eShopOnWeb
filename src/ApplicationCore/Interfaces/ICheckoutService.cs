using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}

public sealed class ReconciliationReport
{
    public ReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ReconciliationMatch> matches,
        IReadOnlyList<ReconciliationMatch> paypalOnly,
        IReadOnlyList<ReconciliationMatch> eShopOnly)
    {
        From = from;
        To = to;
        Matches = matches;
        PaypalOnly = paypalOnly;
        EShopOnly = eShopOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public IReadOnlyList<ReconciliationMatch> Matches { get; }
    public IReadOnlyList<ReconciliationMatch> PaypalOnly { get; }
    public IReadOnlyList<ReconciliationMatch> EShopOnly { get; }
}

public sealed class ReconciliationMatch
{
    public ReconciliationMatch(
        string status,
        int? orderId,
        string? paypalTransactionId,
        string? paypalReferenceId,
        string? invoiceId,
        decimal? amount,
        string? currency)
    {
        Status = status;
        OrderId = orderId;
        PaypalTransactionId = paypalTransactionId;
        PaypalReferenceId = paypalReferenceId;
        InvoiceId = invoiceId;
        Amount = amount;
        Currency = currency;
    }

    public string Status { get; }
    public int? OrderId { get; }
    public string? PaypalTransactionId { get; }
    public string? PaypalReferenceId { get; }
    public string? InvoiceId { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, string merchantCustomerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
