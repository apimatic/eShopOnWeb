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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a follow-up asking
/// how delivery went is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, HttpContext>
{
    private readonly IOrderNotificationService _service;

    public DispatchOrderEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), http);
            })
            .Produces<DispatchOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, HttpContext http)
    {
        var notifications = await _service.DispatchAsync(request.OrderId, http.RequestAborted);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
