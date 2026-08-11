using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Reads a shopper's own orders together with their payment state.</summary>
public interface IOrderQueryService
{
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

public record OrderWithPayment(Order Order, Payment? Payment);
