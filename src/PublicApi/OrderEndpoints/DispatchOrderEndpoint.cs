using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest
{
    public int OrderId { get; set; }
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderLifecycleService service) =>
            {
                await service.DispatchAsync(orderId);
                return Results.Ok(new { orderId, status = "Dispatched" });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderLifecycleService service)
        => Task.FromResult(Results.Ok());
}
