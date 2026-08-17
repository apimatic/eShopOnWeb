using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the "pay for an order" flow: placing an order awaiting payment, authorizing a hold,
/// capturing at fulfilment, cancelling (voiding) before fulfilment, refunding after, and reconciliation.
/// Shopper-scoped operations act only on the caller's own data; operator operations (fulfil/cancel/
/// reconcile) are guarded by the administrator role at the API boundary.
/// </summary>
public interface IPaymentService
{
    Task<Result<PlacedOrder>> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipping, CancellationToken ct = default);

    Task<Result<PaymentView>> AuthorizeAsync(int orderId, string buyerId, PayInput input, CancellationToken ct = default);

    Task<Result<PaymentView>> FulfilAsync(int orderId, CancellationToken ct = default);

    Task<Result<PaymentView>> CancelAsync(int orderId, CancellationToken ct = default);

    Task<Result<RefundView>> RefundAsync(int orderId, string buyerId, RefundInput input, CancellationToken ct = default);

    Task<Result<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
