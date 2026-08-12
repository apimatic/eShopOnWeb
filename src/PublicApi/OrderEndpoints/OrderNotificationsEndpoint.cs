using System.Threading;
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
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. Each entry carries its own notificationId (what the operator endpoints act on). Visible to
/// the order's owner; an operator (administrator) may view any order's notifications.
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
            (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var user = http.User;
        var buyerId = user.Identity!.Name!;
        var isOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

        var notifications = await service.GetOrderNotificationsAsync(orderId, buyerId, isOperator, http.RequestAborted);
        if (notifications is null)
        {
            // Either no such order, or it is not the caller's to see — do not reveal which.
            return Results.NotFound();
        }

        return Results.Ok(new OrderNotificationsResponse { OrderId = orderId, Notifications = notifications });
    }
}
