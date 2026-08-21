using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    Task<Order> PayAsync(string buyerId, int orderId, CardPaymentRequest? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed record CardPaymentRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddressRequest? BillingAddress);

public sealed record CardBillingAddressRequest(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    IReadOnlyList<PayPalReportedTransaction> PayPalTransactions,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<EshopReconciliationEntry> EshopOnly);

public sealed record ReconciliationMatch(
    PayPalReportedTransaction PayPalTransaction,
    int OrderId);

public sealed record EshopReconciliationEntry(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? PayPalAuthorizationId,
    string? PayPalCaptureId);
