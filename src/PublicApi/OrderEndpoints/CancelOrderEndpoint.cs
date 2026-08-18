using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels the order. The shopper is told, and any not-yet-sent
/// delivery follow-up is called off with the provider so it can never reach them. Restricted to the
/// administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IRepository<Order>>
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
            (int orderId, IRepository<Order> orderRepository) =>
                await HandleAsync(new OrderIdRequest(orderId), orderRepository))
            .Produces<OrderStateChangeResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
            return Results.NotFound();

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        // Calls off any not-yet-sent follow-up first, then sends the cancellation notice.
        await _notifications.NotifyOrderCancelledAsync(order, CancellationToken.None);

        return Results.Ok(new OrderStateChangeResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
