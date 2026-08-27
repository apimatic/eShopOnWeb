using System;
using System.Threading;
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

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery
/// follow-up still queued with the provider is called off.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, orderRepository, notificationService, cancellationToken);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"Order {orderId} is already cancelled." });
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order, cancellationToken);

        // Notification failures never fail the cancellation.
        await notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
