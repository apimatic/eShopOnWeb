using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: marks an order dispatched, tells the shopper it is on its way, and queues a
/// delivery follow-up with the provider a few days out.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                try
                {
                    await service.DispatchOrderAsync(orderId, ct);
                    return Results.Ok(new { orderId, status = "dispatched" });
                }
                catch (OrderNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOrderStateException ex)
                {
                    return Results.Conflict(ex.Message);
                }
            })
            .WithTags("OrderNotificationEndpoints");
    }
}
