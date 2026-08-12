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
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a delivery
/// follow-up is queued with the provider for a few days later. Restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _http;

    public DispatchOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications, IHttpContextAccessor http)
    {
        _orders = orders;
        _notifications = notifications;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
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

        order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);

        await _notifications.NotifyOrderDispatchedAsync(order, ct);

        return Results.Ok(new OrderStateResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
