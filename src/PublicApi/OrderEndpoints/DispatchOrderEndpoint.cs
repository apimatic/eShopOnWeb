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

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IRepository<Order> orders) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), http, orders);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IRepository<Order> orders)
        => HandleAsync(request, null!, orders);

    private async Task<IResult> HandleAsync(DispatchOrderRequest request, HttpContext http, IRepository<Order> orders)
    {
        var order = await orders.GetByIdAsync(request.OrderId, http.RequestAborted);
        if (order is null)
        {
            return Results.NotFound();
        }

        order.MarkDispatched();
        await orders.UpdateAsync(order, http.RequestAborted);
        await _notifications.NotifyOrderDispatchedAsync(order, http.RequestAborted);
        var created = await _notifications.ListForOrderAsync(order.Id, http.RequestAborted);

        return Results.Ok(new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = NotificationDtoMapper.ToDtos(created)
        });
    }
}
