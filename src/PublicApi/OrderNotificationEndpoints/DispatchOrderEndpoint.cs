using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/dispatch — operator marks the order dispatched. The shopper is told it is
/// on its way and a delivery follow-up is queued with the provider for a few days later. Administrator only.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderMessagingService service,
                CancellationToken cancellationToken) =>
            {
                var order = await service.DispatchAsync(orderId, cancellationToken);
                return Results.Ok(new OrderStatusResponse { Order = OrderDto.From(order) });
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}
