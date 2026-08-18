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
/// What was sent for one of the caller's own orders, and what became of each message. Each entry carries its
/// own notificationId — what the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, HttpContext>
{
    private readonly IOrderNotificationService _service;

    public OrderNotificationsEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) =>
            {
                return await HandleAsync(new OrderNotificationsRequest(orderId), http);
            })
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.Of(http.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await _service.GetNotificationsForOrderAsync(buyerId, request.OrderId, http.RequestAborted);
        if (notifications is null)
        {
            // Not the caller's order (or it does not exist).
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
