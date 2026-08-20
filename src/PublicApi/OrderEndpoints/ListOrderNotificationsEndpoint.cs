using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(orderId, service, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService service) =>
        HandleAsync(orderId, service, httpContext: null!);

    private async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId()!;
        var notifications = await service.ListOrderNotificationsAsync(orderId, buyerId, httpContext.IsAdministrator(), default);
        if (notifications.Count == 0)
        {
            var orders = await service.ListBuyerOrdersAsync(buyerId, default);
            var ownsOrder = httpContext.IsAdministrator() || orders.Any(o => o.Id == orderId);
            if (!ownsOrder && !httpContext.IsAdministrator())
            {
                return Results.NotFound();
            }
        }

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(ListMyOrdersEndpoint.MapNotification).ToList()
        });
    }
}
