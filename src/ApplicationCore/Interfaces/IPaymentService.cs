using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemRequest> items,
        ShippingAddressDetails? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundOperationResult> RefundAsync(
        int orderId,
        string? buyerId,
        bool isOperator,
        long? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<SavedCardResult> SaveCardAsync(
        string buyerId,
        string? buyerEmail,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardResult>> ListSavedCardsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(
        int paymentMethodId,
        string buyerId,
        CancellationToken cancellationToken = default);
}
