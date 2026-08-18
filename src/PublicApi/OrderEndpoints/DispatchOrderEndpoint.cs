using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record DispatchOrderRequest(int OrderId);

/// <summary>
/// POST /api/orders/{orderId}/dispatch — operator action. Marks the order dispatched: tells the shopper it
/// is on its way and queues a "how did delivery go?" follow-up with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), service);
            })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderNotificationService service)
    {
        var result = await service.DispatchAsync(request.OrderId);
        return result.Outcome switch
        {
            OrderTransitionOutcome.Success => Results.Ok(new OrderTransitionResponse
            {
                OrderId = request.OrderId,
                Status = "dispatched",
                Message = "Order marked dispatched. Any shopper with a number on file was notified and a delivery follow-up was queued."
            }),
            OrderTransitionOutcome.OrderNotFound => Results.NotFound(new { error = "Order not found." }),
            OrderTransitionOutcome.AlreadyDispatched => Results.Conflict(new { error = "Order has already been dispatched." }),
            OrderTransitionOutcome.AlreadyCancelled => Results.Conflict(new { error = "Order has been cancelled and cannot be dispatched." }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
