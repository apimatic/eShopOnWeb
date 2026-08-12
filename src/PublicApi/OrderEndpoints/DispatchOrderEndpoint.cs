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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a delivery
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, CancellationToken cancellationToken) =>
            {
                var outcome = await service.DispatchOrderAsync(orderId, cancellationToken);
                return outcome switch
                {
                    OrderOperationOutcome.Success => Results.Ok(new OrderOperationResponse { OrderId = orderId, Status = "Dispatched" }),
                    OrderOperationOutcome.NotFound => Results.NotFound(),
                    OrderOperationOutcome.InvalidState => Results.Conflict(new { message = "The order cannot be dispatched from its current state." }),
                    _ => Results.Problem()
                };
            })
            .Produces<OrderOperationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
