using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    // Authorizes (holds) the order's total with either a one-off card or one of the buyer's saved
    // cards. Idempotent in effect: calling this again for an already-authorized/fulfilled order
    // returns the existing payment rather than authorizing twice.
    Task<Payment> AuthorizePaymentAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);

    // Captures a previously-authorized order (an operator action). Renews a stale authorization
    // automatically before giving up.
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    // Releases the hold on an order that has not yet been fulfilled (an operator action).
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default);

    // Refunds a fulfilled order's capture, in full (amount == null) or in part. idempotencyKey
    // dedupes retried requests; two distinct partial refunds must use two distinct keys.
    Task<Refund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
