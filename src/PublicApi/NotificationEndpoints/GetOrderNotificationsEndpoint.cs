using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class GetOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                HttpContext context,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetOrderNotificationsAsync(context.User.Identity!.Name!, orderId, cancellationToken);
                return result.Found ? Results.Ok(new { notifications = result.Notifications }) : Results.NotFound();
            })
            .WithTags("Orders");
    }
}
