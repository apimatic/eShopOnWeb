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
/// Operator action: cancels an order. The shopper is told, and any not-yet-sent delivery follow-up is called
/// off so it can never reach them. Idempotent — cancelling an already-cancelled order does not re-notify.
/// Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<OrderStateResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var ct = http.RequestAborted;
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null) return Results.NotFound();

        var changed = order.Cancel();
        if (!changed)
        {
            return Results.Conflict(new OrderStateResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Changed = false
            });
        }

        await orderRepository.UpdateAsync(order, ct);
        // Calls off any pending follow-up and tells the shopper. Never throws for a messaging failure.
        await notifier.NotifyOrderCanceledAsync(order, ct);

        return Results.Ok(new OrderStateResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Changed = true
        });
    }
}
