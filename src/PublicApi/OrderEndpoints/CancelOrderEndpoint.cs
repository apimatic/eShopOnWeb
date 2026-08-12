using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not yet
/// gone out is called off — a cancelled order must never trigger a "how did delivery go?" message.
/// Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _http;

    public CancelOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications, IHttpContextAccessor http)
    {
        _orders = orders;
        _notifications = notifications;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<OrderStateResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var ct = _http.HttpContext!.RequestAborted;

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null)
        {
            return Results.NotFound();
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);

        await _notifications.NotifyOrderCancelledAsync(order, ct);

        return Results.Ok(new OrderStateResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
