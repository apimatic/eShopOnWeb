using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default);
    Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
}
