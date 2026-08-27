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

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the caller's own orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;

    public ListMyOrdersEndpoint(IRepository<Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(user, orderRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<Order> orderRepository)
    {
        var response = new ListMyOrdersResponse();

        var buyerId = user.GetBuyerId();
        if (buyerId == null)
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        response.Orders = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => OrderMapping.ToDto(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();

        return Results.Ok(response);
    }
}
