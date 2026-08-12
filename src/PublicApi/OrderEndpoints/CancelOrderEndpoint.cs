using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off with the provider so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, CancellationToken cancellationToken) =>
            {
                var outcome = await service.CancelOrderAsync(orderId, cancellationToken);
                return outcome switch
                {
                    OrderOperationOutcome.Success => Results.Ok(new OrderOperationResponse { OrderId = orderId, Status = "Cancelled" }),
                    OrderOperationOutcome.NotFound => Results.NotFound(),
                    OrderOperationOutcome.InvalidState => Results.Conflict(new { message = "The order is already cancelled." }),
                    _ => Results.Problem()
                };
            })
            .Produces<OrderOperationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
