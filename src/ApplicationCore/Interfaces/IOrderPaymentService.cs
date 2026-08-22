using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(
        int orderId,
        string callerBuyerId,
        bool callerIsAdministrator,
        RefundOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PayOrderCommand
{
    public int? PaymentMethodId { get; init; }
    public CardPaymentDetails? Card { get; init; }
}

public sealed class CardPaymentDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string Name { get; init; }
    public BillingAddressDetails? BillingAddress { get; init; }
}

public sealed class BillingAddressDetails
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public sealed class RefundOrderCommand
{
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class ReconciliationReport
{
    public required System.DateTimeOffset From { get; init; }
    public required System.DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matches { get; init; }
    public required IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly { get; init; }
    public required IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? LocalPaymentId { get; init; }
    public string Kind { get; init; } = "matched";
}

public sealed class ReconciliationPayPalOnly
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

public sealed class ReconciliationEshopOnly
{
    public required int OrderId { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public required string Status { get; init; }
}
