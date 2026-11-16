using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<OrderSummary> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        Address? shippingAddress, CancellationToken cancellationToken = default);

    Task<PaymentView> PayOrderAsync(string buyerId, int orderId, CardInput? card,
        int? savedPaymentMethodId, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundView> RefundOrderAsync(string buyerId, int orderId, long? amountCents,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
