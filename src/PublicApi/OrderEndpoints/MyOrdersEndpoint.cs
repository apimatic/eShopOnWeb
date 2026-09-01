using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Payment> _paymentRepository;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepository, IReadRepository<Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;
        var response = new MyOrdersResponse();

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(
            new PaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToList()));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        response.Orders = orders.Select(order => new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = paymentsByOrder.TryGetValue(order.Id, out var payment)
                ? PaymentStateDto.FromEntity(payment)
                : null
        }).ToList();

        return Results.Ok(response);
    }
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new List<OrderDto>();
}
