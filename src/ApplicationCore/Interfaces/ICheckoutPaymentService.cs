using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICheckoutPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shipTo,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> RefundAsync(
        int orderId,
        string actorId,
        bool actorIsAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<PayPalReportedTransaction> PayPalOnly,
    IReadOnlyList<ReconciliationEshopEntry> EshopOnly);

public record ReconciliationMatch(
    int OrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    string MatchReason);

public record ReconciliationEshopEntry(
    int OrderId,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    IReadOnlyList<string> RefundIds);
