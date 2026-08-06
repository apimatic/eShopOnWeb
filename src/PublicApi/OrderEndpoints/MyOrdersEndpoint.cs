using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>Returns the signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderRepository);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => o.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
