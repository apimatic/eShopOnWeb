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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderCheckoutService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(service, user);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService service)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(IOrderCheckoutService service, ClaimsPrincipal user)
    {
        var orders = await service.ListMyOrdersAsync(user.GetBuyerId());
        return Results.Ok(new
        {
            orders = orders.Select(o => o.ToResponse())
        });
    }
}
