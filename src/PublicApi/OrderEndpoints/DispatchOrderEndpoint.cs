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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/dispatch — an operator marks the order dispatched. The shopper is told it is on
/// its way, and a "how did the delivery go?" follow-up is queued with the provider for a few days later.
/// Administrator only.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var order = await orderRepository.GetByIdAsync(orderId, ct);
                if (order is null)
                {
                    return Results.NotFound();
                }

                await notifications.NotifyOrderDispatchedAsync(order, ct);
                return Results.Ok(new { orderId = order.Id, status = "dispatched" });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
