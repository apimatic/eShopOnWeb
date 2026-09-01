using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items, Address? shipToAddress, CancellationToken ct = default);

    /// <summary>Returns null when the order does not exist or belongs to another shopper.</summary>
    Task<Order?> PayWithCardAsync(string buyerId, int orderId, CardDetails card, CancellationToken ct = default);

    /// <summary>Returns null when the order does not exist or belongs to another shopper.
    /// Throws <see cref="Exceptions.PaymentGatewayException"/> with Kind NotFound when the saved card is not the caller's.</summary>
    Task<Order?> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken ct = default);

    Task<Order?> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Returns (order, refund); order is null when not found / not the caller's.
    /// A repeated idempotency key returns the original refund without refunding again.</summary>
    Task<(Order? Order, OrderRefund? Refund)> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Returns false when the saved card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
