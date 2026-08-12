using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action. Marks the order cancelled, tells the
/// shopper, and calls off any follow-up that has not yet gone out so it can never reach them.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<OrderStatusRecord> statusRepository,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var statusRecord = await statusRepository.FirstOrDefaultAsync(
                    new OrderStatusRecordByOrderIdSpecification(orderId), cancellationToken);
                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                if (statusRecord is null || order is null)
                    return Results.NotFound();

                try
                {
                    statusRecord.MarkCancelled();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }

                await statusRepository.UpdateAsync(statusRecord, cancellationToken);

                // Calls off any pending follow-up and tells the shopper. Best-effort messaging.
                await notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

                return Results.Ok(new OrderStateResponse { OrderId = orderId, State = statusRecord.State.ToString() });
            })
            .Produces<OrderStateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
