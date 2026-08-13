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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Orders;

/// <summary>Lists the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string, IPublicApiOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPublicApiOrderService service) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                return await HandleAsync(buyerId, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPublicApiOrderService service)
    {
        var results = await service.GetOrdersForBuyerAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = results.Select(r => r.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}

public class MyOrdersResponse
{
    public List<ApiOrderDto> Orders { get; set; } = new();
}
