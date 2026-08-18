using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any follow-up that has not yet gone out is
/// called off so a "how did delivery go?" text can never reach them for a cancelled order.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, HttpContext>
{
    private readonly IOrderNotificationService _service;

    public CancelOrderEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), http);
            })
            .Produces<CancelOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, HttpContext http)
    {
        var notifications = await _service.CancelAsync(request.OrderId, http.RequestAborted);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
