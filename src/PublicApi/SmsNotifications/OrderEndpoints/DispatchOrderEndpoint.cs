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

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a
/// "how did delivery go?" follow-up is queued with the provider for a few days later.
/// Restricted to the administrator role.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
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

                // May throw InvalidOrderStateException (already dispatched / cancelled) -> 409.
                order.MarkDispatched();
                await orderRepository.UpdateAsync(order, cancellationToken);

                await notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

                return Results.Ok(new DispatchOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
