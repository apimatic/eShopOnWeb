using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-payment lifecycle over the existing order model and the PayPal gateway.
/// Each action is separately invocable; none does more than one step of the flow.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id. No payment yet.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total against a one-off card or a saved card. Idempotent in effect.</summary>
    Task<PaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, Guid? savedPaymentMethodId,
        CancellationToken ct);

    /// <summary>Operator: fulfils the order — captures the held funds, renewing a stale authorization if needed.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment — voids the hold so no money moves.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the captured payment, full or partial, under a caller idempotency key. Returns the refund id.</summary>
    Task<Guid> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
}
