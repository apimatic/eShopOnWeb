using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the authenticated shopper's orders with their payment state.</summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService orderPaymentService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(orderPaymentService, user, cancellationToken))
            .Produces<ListMyOrdersResponse>(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        IOrderPaymentService orderPaymentService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);

        var response = new ListMyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(OrderDto.FromOrder)
                .ToList()
        };

        return Results.Ok(response);
    }
}
