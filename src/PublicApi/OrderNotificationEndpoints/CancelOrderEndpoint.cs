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
/// Operator action: cancels an order, tells the shopper, and calls off any pending follow-up so it
/// can never reach the customer for a cancelled order.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                try
                {
                    await service.CancelOrderAsync(orderId, ct);
                    return Results.Ok(new { orderId, status = "cancelled" });
                }
                catch (OrderNotFoundException)
                {
                    return Results.NotFound();
                }
            })
            .WithTags("OrderNotificationEndpoints");
    }
}
