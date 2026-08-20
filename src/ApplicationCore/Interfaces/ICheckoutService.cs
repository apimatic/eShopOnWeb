using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderPayment Payment)> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderPayment Payment)> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderPayment? Payment)> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderPayment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, RefundOrderRequest request, bool callerIsAdministrator, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Order Order, OrderPayment? Payment)>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed class PayOrderRequest
{
    public int? PaymentMethodId { get; init; }
    public CardPaymentDetails? Card { get; init; }
}

public sealed class CardPaymentDetails
{
    public string? Name { get; init; }
    public string? Number { get; init; }
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public sealed class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public sealed class RefundOrderRequest
{
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class ReconciliationReport
{
    public required System.DateTimeOffset From { get; init; }
    public required System.DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matches { get; init; }
    public required IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; }
    public required IReadOnlyList<OrderPayment> EshopOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required PayPalReportedTransaction PayPalTransaction { get; init; }
    public required OrderPayment EshopPayment { get; init; }
    public int OrderId { get; init; }
}
