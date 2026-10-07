using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items at catalog prices; the order starts awaiting payment.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderLine>? lines, Address? shipToAddress,
        CancellationToken cancellationToken);

    /// <summary>Charges the order total to the card now. A buyer can only pay their own order.</summary>
    Task<PayOrderResult> PayAsync(string buyerId, int orderId, EncryptedCard card, CancellationToken cancellationToken);

    /// <summary>Gives money back on a paid order, never beyond what was paid.</summary>
    Task<RefundOrderResult> RefundAsync(int orderId, decimal amount, RefundReason? reason, string? clientRequestKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken);
}
