using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog items (the API caller has no server-side basket), reusing the
/// existing Order/OrderItem model. The new order starts awaiting payment.
/// </summary>
public interface IOrderCheckoutService
{
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address? shipToAddress, CancellationToken cancellationToken);
}
