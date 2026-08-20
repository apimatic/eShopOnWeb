using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentCheckoutService
{
    string Currency { get; }

    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, Address shippingAddress, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardPaymentDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class OrderLineInput
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
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
    public required string Status { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = Array.Empty<string>();
}
