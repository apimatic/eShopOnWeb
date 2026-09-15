using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: authorize (hold) at pay time, capture (take) at
/// fulfilment, void (release) on cancel, and refund after fulfilment. Each operation is idempotent
/// in effect — a double-click never authorizes or captures the shopper twice.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places a new order for the buyer from catalog item ids and quantities. The order starts
    /// awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, System.Collections.Generic.IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken ct = default);

    /// <summary>Authorizes the order total against a one-off card or one of the buyer's saved cards.</summary>
    Task<Order> PayAsync(int orderId, string buyerId, PaymentInstrument instrument, CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels an order before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a fulfilled order, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>How to pay: either raw card details for a one-off payment, or a saved card id.</summary>
public record PaymentInstrument(CardDetails? Card, int? SavedPaymentMethodId)
{
    public bool UsesSavedCard => SavedPaymentMethodId.HasValue;
}
