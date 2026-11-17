using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IOrderPaymentService
{
    Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken);

    Task<OrderDto> AuthorizeOrderAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken);

    Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundDto> RefundOrderAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReport> GetReconciliationAsync(
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<SavedPaymentMethodDto> SavePaymentMethodAsync(
        string ownerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(
        string ownerId,
        CancellationToken cancellationToken);

    Task DeleteSavedPaymentMethodAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken);
}
