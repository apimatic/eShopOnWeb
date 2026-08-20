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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IRepository<Order> orders) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), http, orders);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orders)
        => HandleAsync(request, null!, orders);

    private async Task<IResult> HandleAsync(CancelOrderRequest request, HttpContext http, IRepository<Order> orders)
    {
        var order = await orders.GetByIdAsync(request.OrderId, http.RequestAborted);
        if (order is null)
        {
            return Results.NotFound();
        }

        order.MarkCancelled();
        await orders.UpdateAsync(order, http.RequestAborted);
        await _notifications.NotifyOrderCancelledAsync(order, http.RequestAborted);
        var created = await _notifications.ListForOrderAsync(order.Id, http.RequestAborted);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = NotificationDtoMapper.ToDtos(created)
        });
    }
}
