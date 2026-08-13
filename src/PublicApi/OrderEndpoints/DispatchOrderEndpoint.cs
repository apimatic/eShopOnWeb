using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a "how did
/// delivery go" follow-up is queued with the provider for a few days later. Restricted to administrators.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ISmsNotificationService service) =>
                await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, service))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, ISmsNotificationService service)
    {
        var result = await service.DispatchOrderAsync(request.OrderId);
        return result switch
        {
            OrderTransitionResult.Success => Results.Ok(new { orderId = request.OrderId, status = "dispatched" }),
            OrderTransitionResult.AlreadyInState => Results.Conflict(new { orderId = request.OrderId, message = "Order has already been dispatched." }),
            _ => Results.NotFound(new { orderId = request.OrderId, message = "Order not found." })
        };
    }
}
