using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.Contracts;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for one of the caller's orders and what became of each message. Each entry carries
/// its own notificationId — what the operator endpoints act on.
/// GET /api/orders/{orderId}/notifications
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) => await HandleAsync(orderId, service))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        var buyerId = _httpContextAccessor.RequireUserId();
        var notifications = await service.GetOrderNotificationsAsync(orderId, buyerId);
        if (notifications is null) return Results.NotFound();

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
