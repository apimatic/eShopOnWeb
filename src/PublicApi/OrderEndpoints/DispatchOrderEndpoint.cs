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

public class OrderStateResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Changed { get; set; }
}

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a "how did delivery
/// go?" follow-up is queued with the provider for a few days later. Idempotent — a re-dispatch does not
/// re-notify. Restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
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

        // Gate the outbound notification on a real state change (no duplicate messages on re-dispatch).
        var changed = order.Dispatch();
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
        await notifier.NotifyOrderDispatchedAsync(order, ct);

        return Results.Ok(new OrderStateResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Changed = true
        });
    }
}
