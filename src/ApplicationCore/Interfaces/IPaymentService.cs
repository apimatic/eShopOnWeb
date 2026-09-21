using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line of a new order: a catalog item id and a quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money flows over the domain model and the <see cref="IPaymentGateway"/>:
/// placing orders, authorizing/capturing/voiding/refunding payments, saved cards, and reconciliation.
/// </summary>
public interface IPaymentService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken ct);

    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct);

    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
