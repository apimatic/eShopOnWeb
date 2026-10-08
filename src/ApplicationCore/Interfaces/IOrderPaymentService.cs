using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>The currency orders are priced and charged in.</summary>
    string Currency { get; }

    /// <summary>Places an order for catalog items, priced from the catalog. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken);

    /// <summary>Charges the order total to the card. Never charges an order twice.</summary>
    Task<PayOrderResult> PayAsync(int orderId, string buyerId, CardDetails card, CancellationToken cancellationToken);

    /// <summary>Gives back all (amount null) or part of what was paid for the order.</summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string requestedBy, string? clientRequestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken);
}

public record OrderLine(int CatalogItemId, int Quantity);

public enum PayOrderOutcome
{
    /// <summary>This request took the money.</summary>
    Paid,
    /// <summary>The order had already been paid; nothing was charged again.</summary>
    AlreadyPaid,
    /// <summary>The card was not charged; <see cref="OrderPayment.ShopperMessage"/> says what the shopper can do.</summary>
    Declined,
    /// <summary>The provider has not given a final answer yet.</summary>
    Pending
}

public record PayOrderResult(PayOrderOutcome Outcome, Order Order, OrderPayment Payment);

/// <param name="Replayed">True when the operator's Idempotency-Key matched an earlier refund, which is returned instead.</param>
public record RefundOrderResult(Order Order, OrderRefund Refund, bool Replayed);
