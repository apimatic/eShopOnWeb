using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of PayPal: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Enforces shopper ownership and idempotency. Operations that are operator-only
/// (fulfil / cancel) do not take a buyerId; the API layer restricts them to the administrator role.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog lines for the shopper, in the awaiting-payment state. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (places a hold for) the order total. Idempotent: a repeat never places a second hold.</summary>
    Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the hold at fulfilment, renewing a stale authorization first if needed.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: releases the hold before fulfilment so no money moves.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads the payment for an order the caller owns (throws if not found / not theirs).</summary>
    Task<OrderPayment> GetOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
