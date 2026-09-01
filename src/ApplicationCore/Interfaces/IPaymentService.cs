using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the buyer; it starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct);

    /// <summary>Authorizes the order total with raw card details (a hold; no money moves).</summary>
    Task<Payment> PayWithCardAsync(string buyerId, int orderId, CardPaymentDetails card, CancellationToken ct);

    /// <summary>Authorizes the order total with one of the buyer's saved cards.</summary>
    Task<Payment> PayWithSavedCardAsync(string buyerId, int orderId, int savedCardId, CancellationToken ct);

    /// <summary>Operator: fulfils the order and captures the held money, renewing a stale authorization first.</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: refunds a captured payment, in full (amount null) or in part, under a caller idempotency key.</summary>
    Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken ct);

    Task<IReadOnlyList<Payment>> ListBuyerPaymentsAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: lines PayPal's own transaction record up against eShop orders over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);
