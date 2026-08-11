using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderQueryService : IOrderQueryService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Payment> _paymentRepository;

    public OrderQueryService(IReadRepository<Order> orderRepository, IReadRepository<Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, paymentByOrder.TryGetValue(o.Id, out var payment) ? payment : null))
            .ToList();
    }
}
