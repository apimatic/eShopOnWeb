using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(service, user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => HandleAsync(service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(IOrderNotificationService service, ClaimsPrincipal user)
    {
        var unauthorized = user.RequireBuyerId(out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var orders = await service.GetMyOrdersAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(ShopperOrderDto.From).ToList()
        });
    }
}

public class ListMyOrdersResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}
