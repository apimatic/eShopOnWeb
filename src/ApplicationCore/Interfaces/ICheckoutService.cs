using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress, CancellationToken cancellationToken = default);

    Task<OrderPayment> PayAsync(string buyerId, int orderId, CardPaymentRequest? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrder>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed class CardPaymentRequest
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public PayPalBillingAddress? BillingAddress { get; init; }
}

public sealed class ShopperOrder
{
    public required Order Order { get; init; }
    public OrderPayment? Payment { get; init; }
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationRow> Matched { get; init; }
    public required IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; }
    public required IReadOnlyList<OrderPayment> EshopOnly { get; init; }
}

public sealed class ReconciliationRow
{
    public required OrderPayment Payment { get; init; }
    public required PayPalReportedTransaction Transaction { get; init; }
}
