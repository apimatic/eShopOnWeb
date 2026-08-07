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

public class MyOrdersResponse
{
    public List<OrderPaymentStateDto> Orders { get; set; } = new();
}

/// <summary>Lists the authenticated shopper's orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                return await HandleAsync(user, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints")
            .WithSummary("List the caller's orders and their payment state");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetOrdersForBuyerAsync(buyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(OrderPaymentStateDto.From).ToList()
        });
    }
}
