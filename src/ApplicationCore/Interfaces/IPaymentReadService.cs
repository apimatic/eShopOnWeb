using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Reads the caller's orders alongside their payment state.</summary>
public interface IPaymentReadService
{
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>An order paired with its payment (null when the order has never been paid).</summary>
public record OrderPaymentView(Order Order, Payment? Payment);
