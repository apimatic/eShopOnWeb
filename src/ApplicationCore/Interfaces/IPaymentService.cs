using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Result of a refund: the updated order plus the refund that was recorded.</summary>
public record RefundOutcome(Order Order, PaymentRefund Refund);

/// <summary>
/// Orchestrates the pay-for-an-order flow: placing an order, authorizing (holding) the total,
/// capturing at fulfilment, cancelling (releasing the hold), and refunding.
/// Every money operation is idempotent in effect.
/// </summary>
public interface IPaymentService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> AuthorizeAsync(
        string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<RefundOutcome>> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Order>>> GetOrdersForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default);
}
