using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, CreateOrderCommand command, CancellationToken cancellationToken);

    Task<Order> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<Refund> RefundAsync(int orderId, RefundCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken);
}

public interface IPaymentMethodService
{
    Task<SavedCard> SaveAsync(string buyerId, SaveCardCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}

public interface IOrderOperationLock
{
    Task<IDisposable> AcquireAsync(int orderId, CancellationToken cancellationToken);
}
