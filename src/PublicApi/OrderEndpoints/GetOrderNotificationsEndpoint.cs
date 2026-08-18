using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record GetOrderNotificationsRequest(int OrderId);

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for one of the caller's own orders and what
/// became of each message. Each entry carries its own notificationId (what operator endpoints act on).
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService service)
    {
        var ownerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.GetOrderNotificationsAsync(ownerId, request.OrderId, EndpointCaller.RequestAborted(_httpContextAccessor));
        if (notifications is null)
        {
            // Not the caller's order (or it does not exist) — one shopper never sees another's.
            return Results.NotFound(new { error = "Order not found." });
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        };
        return Results.Ok(response);
    }
}
