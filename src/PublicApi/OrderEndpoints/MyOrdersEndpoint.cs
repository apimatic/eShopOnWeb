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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the signed-in shopper's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>, ClaimsPrincipal, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository, CancellationToken ct) =>
                await HandleAsync(orderRepository, user, ct))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(OrderDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
