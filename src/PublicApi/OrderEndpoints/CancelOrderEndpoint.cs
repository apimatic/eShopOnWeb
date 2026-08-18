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
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off so a cancelled order can never trigger a "how did delivery go?" text.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService notificationService) =>
                await HandleAsync(new CancelOrderRequest(orderId), notificationService))
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderNotificationService notificationService)
    {
        var cancelled = await notificationService.NotifyCancelledAsync(request.OrderId);
        if (!cancelled)
        {
            return Results.NotFound();
        }

        return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = request.OrderId });
    }
}
