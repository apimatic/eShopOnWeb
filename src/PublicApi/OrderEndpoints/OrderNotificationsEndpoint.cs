using System.Collections.Generic;
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
/// What was sent for one order and what became of each message. Each entry carries its own
/// notificationId — that is what the operator endpoints act on. Scoped to the order owner (an
/// operator may view any order).
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
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service) => await HandleAsync(orderId, service))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service)
    {
        var user = _httpContextAccessor.HttpContext!.User;
        var callerId = user.Identity!.Name!;
        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

        var result = await service.GetOrderNotificationsAsync(orderId, callerId, isAdmin);
        return result.Outcome switch
        {
            ActionOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            ActionOutcome.Forbidden => Results.Forbid(),
            _ => Results.Ok(new OrderNotificationsResponse { OrderId = orderId, Notifications = result.Notifications })
        };
    }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = System.Array.Empty<NotificationView>();
}
