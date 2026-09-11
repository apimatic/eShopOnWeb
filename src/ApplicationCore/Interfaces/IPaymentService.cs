using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement around an order: place (awaiting payment), authorize (hold),
/// fulfil (capture), cancel (void) and refund. Each action is separately invocable and idempotent
/// in effect. Ordering/refund access is scoped to the calling shopper; fulfil/cancel are operator
/// actions and act on any order.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog item ids + quantities, awaiting payment. Returns the new order id.</summary>
    Task<Result<int>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorize (hold) the order total with PayPal using a card or a saved card.</summary>
    Task<Result<Payment>> PayOrderAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default);

    /// <summary>Operator: fulfil the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<Result<Payment>> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel before fulfilment, voiding the hold so no money moved.</summary>
    Task<Result<Payment>> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refund a captured payment in full or in part, under a caller-supplied idempotency key.</summary>
    Task<Result<PaymentRefund>> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's orders together with their payment state.</summary>
    Task<Result<IReadOnlyList<OrderWithPayment>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}

/// <summary>One requested order line.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>How to pay: either raw card details, or the id of one of the caller's saved cards.</summary>
public class PayInstruction
{
    public GatewayCardDetails? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>An order paired with its payment (if any) for the my-orders view.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);
