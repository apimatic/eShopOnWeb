using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels the order. The shopper is told, and a follow-up that
/// has not yet gone out is called off so it can never reach them.
/// </summary>
public class CancelOrderEndpoint
    : IEndpoint<IResult, int, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(orderId, service, http))
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, HttpContext http)
    {
        var result = await service.CancelAsync(orderId, http.RequestAborted);
        return result.Status switch
        {
            OrderActionStatus.NotFound => Results.NotFound(),
            OrderActionStatus.InvalidState => Results.Conflict(new { error = result.Message }),
            _ => Results.Ok(new OrderActionResponse { OrderId = orderId, Status = "Cancelled" })
        };
    }
}
