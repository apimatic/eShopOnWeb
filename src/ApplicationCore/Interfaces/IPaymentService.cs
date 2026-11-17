using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    Task<CreatedOrderResult> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        AddressInput? shippingAddress,
        CancellationToken cancellationToken = default);

    Task<PaymentActionResult> PayOrderAsync(
        string buyerId,
        int orderId,
        CardDetailsInput? card,
        string? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<FulfilmentResult> FulfilOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<CancelResult> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundOrderAsync(
        string callerId,
        bool isAdministrator,
        int orderId,
        decimal? amount,
        long? amountInCents,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryResult>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<SavedPaymentMethodResult> SavePaymentMethodAsync(
        string buyerId,
        CardDetailsInput card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethodResult>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePaymentMethodAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<ReconciliationResult> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
