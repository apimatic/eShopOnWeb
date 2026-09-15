using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels the order. Texts the shopper and calls off
/// any follow-up that has not yet gone out. Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var order = await service.CancelOrderAsync(orderId, cancellationToken);
                    if (order == null) return Results.NotFound();
                    return Results.Ok(new OrderStatusResponse(order.Id, order.Status.ToString()));
                }
                catch (InvalidOrderStateException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderNotificationEndpoints");
    }
}
