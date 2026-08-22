using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, orders, httpContext);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IRepository<Order> orders)
        => HandleAsync(orderId, orders, null!);

    private async Task<IResult> HandleAsync(int orderId, IRepository<Order> orders, HttpContext httpContext)
    {
        var order = await orders.GetByIdAsync(orderId, httpContext.RequestAborted);
        if (order is null)
        {
            return Results.NotFound();
        }

        order.MarkDispatched();
        await orders.UpdateAsync(order, httpContext.RequestAborted);
        await _notifications.NotifyOrderDispatchedAsync(order, httpContext.RequestAborted);

        return Results.Ok(new DispatchOrderResponse
        {
            OrderId = order.Id,
            Status = order.FulfillmentStatus.ToString()
        });
    }
}
