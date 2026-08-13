using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order. The shopper is told, and any not-yet-sent follow-up is called
/// off so it can never reach them.
/// POST /api/orders/{orderId}/cancel
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) => await HandleAsync(orderId, service))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        var cancelled = await service.CancelAsync(orderId);
        if (!cancelled) return Results.NotFound();
        return Results.Ok(new { orderId, status = "Cancelled" });
    }
}
