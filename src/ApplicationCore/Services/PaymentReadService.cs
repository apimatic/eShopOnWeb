using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReadService : IPaymentReadService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Payment> _paymentRepository;

    public PaymentReadService(IReadRepository<Order> orderRepository, IReadRepository<Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

        var views = new List<OrderPaymentView>(orders.Count);
        foreach (var order in orders)
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(order.Id), cancellationToken);
            views.Add(new OrderPaymentView(order, payment));
        }

        return views;
    }
}
