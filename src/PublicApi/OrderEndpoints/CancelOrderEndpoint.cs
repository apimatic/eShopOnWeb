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
/// Operator action: cancel an order. The shopper is told, and any delivery follow-up that has not yet
/// gone out is called off so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
            {
                var result = await service.CancelAsync(orderId, ct);
                return result switch
                {
                    OrderActionResult.Success => Results.Ok(new OrderActionResponse { OrderId = orderId, Status = "Cancelled" }),
                    OrderActionResult.NotFound => Results.NotFound(),
                    OrderActionResult.InvalidState => Results.Conflict(new { message = "The order is already cancelled." }),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
                };
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult<IResult>(Results.Empty);
}
