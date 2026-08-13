using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any follow-up that has not yet gone out is
/// called off with the provider so it can never reach them. Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ISmsNotificationService service) =>
                await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ISmsNotificationService service)
    {
        var result = await service.CancelOrderAsync(request.OrderId);
        return result switch
        {
            OrderTransitionResult.Success => Results.Ok(new { orderId = request.OrderId, status = "cancelled" }),
            OrderTransitionResult.AlreadyInState => Results.Conflict(new { orderId = request.OrderId, message = "Order has already been cancelled." }),
            _ => Results.NotFound(new { orderId = request.OrderId, message = "Order not found." })
        };
    }
}
