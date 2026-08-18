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
/// POST /api/orders/{orderId}/dispatch — operator marks the order dispatched. The shopper is told it is on its
/// way, and a "how did the delivery go?" follow-up is queued with the provider for a few days later.
/// Restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IRepository<Order>>
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

        order.MarkDispatched();
        await orderRepository.UpdateAsync(order);

        await _notifications.NotifyOrderDispatchedAsync(order, CancellationToken.None);

        return Results.Ok(new OrderStateChangeResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
