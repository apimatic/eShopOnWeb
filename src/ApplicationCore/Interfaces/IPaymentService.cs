using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of an order being placed: a catalog item and how many.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// How a shopper pays: exactly one of a raw <see cref="Card"/> for a one-off payment, or the id of
/// one of their saved cards (Flow 2).
/// </summary>
public record PayInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>An order paired with its payment/fulfilment state, for the my-orders view.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>The result of a refund: the refund created (or replayed) and the payment's updated state.</summary>
public record RefundOutcome(Payment Payment, PaymentRefund Refund);

/// <summary>
/// Orchestrates the money movement that follows an order: place, authorize (hold), fulfil (capture),
/// cancel (release), and refund. Ownership and operator scoping are enforced by the caller.
/// </summary>
public interface IPaymentService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct);

    Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct);

    Task<Payment> FulfilAsync(int orderId, CancellationToken ct);

    Task<Payment> CancelAsync(int orderId, CancellationToken ct);

    Task<RefundOutcome> RefundAsync(int orderId, string requesterBuyerId, bool isAdmin, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}
