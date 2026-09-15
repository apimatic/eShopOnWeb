using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orders) =>
            {
                return await HandleAsync(orderId, orders);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orders)
    {
        var order = await orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        order.MarkCancelled();
        await orders.UpdateAsync(order);
        await _notifications.NotifyOrderCancelledAsync(order);

        return Results.Ok(new OrderStatusResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}
