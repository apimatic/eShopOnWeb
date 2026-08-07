using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

/// <summary>Returns the calling shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository, CancellationToken ct) =>
            {
                return await HandleAsync(orderRepository, user.GetBuyerId(), ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    // Required by IEndpoint; the routed lambda calls the buyer-scoped overload instead.
    public Task<IResult> HandleAsync(IReadRepository<Order> orderRepository)
        => HandleAsync(orderRepository, string.Empty, default);

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository, string buyerId, CancellationToken ct)
    {
        var response = new MyOrdersResponse();

        if (!string.IsNullOrEmpty(buyerId))
        {
            var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
            response.Orders = orders.Select(OrderDto.From).ToList();
        }

        return Results.Ok(response);
    }
}
