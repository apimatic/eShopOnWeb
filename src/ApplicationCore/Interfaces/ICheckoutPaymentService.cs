using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutPaymentService
{
    Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Order> AuthorizePaymentAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ShopperPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperPaymentMethod>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matches { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalOnlyTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalOnlyTransaction>();
    public IReadOnlyList<EshopOnlyPayment> EshopOnly { get; init; } = Array.Empty<EshopOnlyPayment>();
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? MatchedOn { get; init; }
}

public sealed class PayPalOnlyTransaction
{
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public sealed class EshopOnlyPayment
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();
}
