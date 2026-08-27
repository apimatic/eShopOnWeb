using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderSummaryDto> CreateOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorize the order total with either raw card details or one of the buyer's saved cards.</summary>
    Task<PaymentDto> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator: capture the authorized funds, renewing a stale authorization when possible.</summary>
    Task<PaymentDto> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel before fulfilment, releasing the shopper's held funds.</summary>
    Task<OrderSummaryDto> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured payment, in full (amount == null) or in part. Idempotent per idempotencyKey.</summary>
    Task<RefundDto> RefundOrderAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<OrderSummaryDto>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    Task<SavedCardDto> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardDto>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);

    Task<ReconciliationDto> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
