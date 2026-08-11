using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The order-payment lifecycle: place an order awaiting payment, authorize (hold),
/// fulfil (capture), cancel (release), and refund (return). Each action is separately
/// invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items, awaiting payment. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total using a one-off card or a saved card.</summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Fulfils the order — captures the held money. Renews a stale hold first if needed. Operator action.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order before fulfilment — releases the hold. Operator action.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured order, fully or partially, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each paired with its payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);
}

/// <summary>One catalog item and how many of it to order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay: either raw <see cref="Card"/> details for a one-off payment, or the id
/// of one of the shopper's saved cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order together with its payment (which may be null before authorization).</summary>
public record OrderPaymentView(Order Order, Payment? Payment);
