using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, orders, httpContext);
            })
            .Produces<CancelOrderResponse>()
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

        order.MarkCancelled();
        await orders.UpdateAsync(order, httpContext.RequestAborted);
        await _notifications.NotifyOrderCancelledAsync(order, httpContext.RequestAborted);

        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = order.FulfillmentStatus.ToString()
        });
    }
}
