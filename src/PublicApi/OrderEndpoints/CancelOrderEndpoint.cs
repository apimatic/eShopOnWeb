using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record CancelOrderRequest(int OrderId);

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action. Cancels the order: tells the shopper and calls off
/// any follow-up that has not yet gone out, so asking how delivery went can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service);
            })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService service)
    {
        var result = await service.CancelAsync(request.OrderId);
        return result.Outcome switch
        {
            OrderTransitionOutcome.Success => Results.Ok(new OrderTransitionResponse
            {
                OrderId = request.OrderId,
                Status = "cancelled",
                Message = "Order cancelled. Any shopper with a number on file was notified and pending delivery follow-ups were called off."
            }),
            OrderTransitionOutcome.OrderNotFound => Results.NotFound(new { error = "Order not found." }),
            OrderTransitionOutcome.AlreadyCancelled => Results.Conflict(new { error = "Order has already been cancelled." }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
