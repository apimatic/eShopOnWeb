using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    string Currency { get; }

    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorizes the order total with either full card details or one of the shopper's saved cards.</summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator: captures the authorized payment (reauthorizing first if the hold went stale).</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancels before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a fulfilled order, in full (amount null) or in part. Idempotent per idempotencyKey.</summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record OrderItemRequest(int CatalogItemId, int Quantity);
