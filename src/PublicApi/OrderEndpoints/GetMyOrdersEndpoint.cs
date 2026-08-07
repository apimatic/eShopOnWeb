using System.Linq;
using System.Security.Claims;
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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var orderRepository = http.RequestServices.GetRequiredService<IReadRepository<Order>>();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        var response = new GetMyOrdersResponse
        {
            Orders = orders.Select(OrderDto.FromEntity).ToList()
        };

        return Results.Ok(response);
    }
}
