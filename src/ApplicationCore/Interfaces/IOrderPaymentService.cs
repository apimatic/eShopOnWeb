using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow on top of the existing order model. Domain outcomes are
/// returned as <see cref="Result"/>; PayPal failures propagate as
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the caller from catalog items; it starts awaiting payment.</summary>
    Task<Result<OrderPlaced>> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? address, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total for the caller's order. Idempotent per order.</summary>
    Task<Result<PaymentView>> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default);

    /// <summary>Operator: fulfils the order, capturing the held funds (renewing a stale hold if needed).</summary>
    Task<Result<PaymentView>> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancels before fulfilment, releasing any held funds.</summary>
    Task<Result<PaymentView>> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds the caller's fulfilled order, in full or in part, idempotently by key.</summary>
    Task<Result<RefundResult>> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<Result<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}
