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

/// <summary>
/// Operator action: cancels an order. The shopper is told, and a follow-up that has not yet gone out is
/// called off so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(orderId, service, http.RequestAborted))
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, CancellationToken ct)
    {
        var result = await service.CancelOrderAsync(orderId, ct);
        return result switch
        {
            OrderTransition.NotFound => Results.NotFound(),
            OrderTransition.NoChange => Results.Ok(new { orderId, status = "Cancelled", changed = false }),
            _ => Results.Ok(new { orderId, status = "Cancelled", changed = true })
        };
    }
}
