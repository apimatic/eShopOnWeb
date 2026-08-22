using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PlaceOrderRequest
{
    public required string BuyerId { get; init; }
    public required IReadOnlyList<PlaceOrderItem> Items { get; init; }
    public Address? ShipTo { get; init; }
}

public sealed class PayOrderRequest
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public CardPaymentSource? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class RefundOrderRequest
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public interface ICheckoutPaymentService
{
    Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<Order> PayOrderAsync(PayOrderRequest request, CancellationToken cancellationToken = default);
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(RefundOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matches { get; init; }
    public required IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; }
    public required IReadOnlyList<ReconciliationEshopEntry> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required int OrderId { get; init; }
    public required PayPalReportedTransaction PayPalTransaction { get; init; }
}

public sealed class ReconciliationEshopEntry
{
    public required int OrderId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
}
