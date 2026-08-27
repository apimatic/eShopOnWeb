using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up
/// that has not gone out yet is cancelled at the provider so it never reaches them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        var orderRepository = httpContext.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict($"Order {orderId} is already cancelled.");
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        await notificationService.NotifyOrderCancelledAsync(order, httpContext.RequestAborted);

        return Results.Ok(new OrderActionResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
