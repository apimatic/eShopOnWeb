using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderWithPayment(Order Order, Payment? Payment);

public interface IOrderQueryService
{
    /// <summary>The caller's orders, newest first, each with its payment (when one exists).</summary>
    Task<IReadOnlyList<OrderWithPayment>> ListOrdersWithPaymentsAsync(string buyerId, CancellationToken ct);
}
