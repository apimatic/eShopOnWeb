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
/// POST /api/orders/{orderId}/cancel — operator cancels the order. The shopper is told, and any
/// not-yet-sent delivery follow-up is called off so it can never reach them. Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderMessagingService service,
                CancellationToken cancellationToken) =>
            {
                var order = await service.CancelAsync(orderId, cancellationToken);
                return Results.Ok(new OrderStatusResponse { Order = OrderDto.From(order) });
            })
            .Produces<OrderStatusResponse>()
            .WithTags("OrderEndpoints");
    }
}
