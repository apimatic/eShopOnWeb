using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order, tells the shopper, and calls off any follow-up that has not yet
/// gone out so a cancelled order is never asked how its delivery went.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
                await HandleAsync(orderId, orderRepository))
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        var ct = _httpContextAccessor.RequestAborted();

        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        // The underlying operation (marking cancelled) must succeed regardless of messaging.
        order.MarkCancelled();
        await orderRepository.UpdateAsync(order, ct);

        // Call off the follow-up first, then tell the shopper the order was cancelled.
        var notificationService = _httpContextAccessor.RequestService<IOrderNotificationService>();
        await notificationService.NotifyOrderCancelledAsync(order, ct);

        return Results.Ok(new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
