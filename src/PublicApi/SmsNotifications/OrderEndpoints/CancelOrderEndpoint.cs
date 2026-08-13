using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off with the provider so it can never reach them. Restricted to the
/// administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                if (order is null)
                    return Results.NotFound();

                // May throw InvalidOrderStateException (already cancelled) -> 409.
                order.MarkCancelled();
                await orderRepository.UpdateAsync(order, cancellationToken);

                await notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

                return Results.Ok(new CancelOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
