using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPaymentService
{
    Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken);

    Task<OrderView> PayAsync(int orderId, string buyerId, PayOrderInput input,
        CancellationToken cancellationToken);

    Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<RefundCreated> RefundAsync(int orderId, string buyerId, RefundInput input,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);

    Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
