using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the app's existing order model and the PayPal gateway.
/// Every operation is idempotent in effect and, where it touches a shopper's order, scoped to that shopper.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the caller; it starts awaiting payment.</summary>
    Task<Result<PaymentDetailsViewModel>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo,
        CancellationToken cancellationToken);

    /// <summary>Authorizes the order total — holds the money without taking it. Scoped to the caller's order.</summary>
    Task<Result<PaymentDetailsViewModel>> AuthorizeAsync(
        string buyerId,
        int orderId,
        PayInstruction instruction,
        CancellationToken cancellationToken);

    /// <summary>Operator action: fulfils the order and captures the held funds.</summary>
    Task<Result<PaymentDetailsViewModel>> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken);

    /// <summary>Operator action: cancels an order before fulfilment, releasing the held funds.</summary>
    Task<Result<PaymentDetailsViewModel>> CancelAsync(
        int orderId,
        CancellationToken cancellationToken);

    /// <summary>Refunds a captured order in full or in part. Scoped to the caller's order.</summary>
    Task<Result<RefundViewModel>> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<PaymentDetailsViewModel>> GetOrdersForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken);

    /// <summary>Operator action: reconciles PayPal's transactions against eShop orders for a date range.</summary>
    Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
