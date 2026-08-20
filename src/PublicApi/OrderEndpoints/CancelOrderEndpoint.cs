using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderLifecycleService service) =>
            {
                await service.CancelAsync(orderId);
                return Results.Ok(new { orderId, status = "Cancelled" });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderLifecycleService service)
        => Task.FromResult(Results.Ok());
}
