using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentWorkflowService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, Address shippingAddress, CancellationToken ct);
    Task<Order> PayAsync(int orderId, string buyerId, CardInput? card, int? paymentMethodId, CancellationToken ct);
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken ct);
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, string alias, CardInput card, CancellationToken ct);
    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct);
    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    CardAddressInput BillingAddress);

public sealed record CardAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries,
    bool ReportingLagPossible);

public sealed record ReconciliationEntry(
    string? PayPalTransactionId,
    int? OrderId,
    string? InvoiceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? OccurredAt,
    string MatchStatus);
