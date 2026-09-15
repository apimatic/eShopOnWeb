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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IPublicApiOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPublicApiOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, orderService, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IPublicApiOrderService orderService)
        => HandleAsync(orderId, orderService, null!);

    private async Task<IResult> HandleAsync(int orderId, IPublicApiOrderService orderService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        await orderService.GetOrderForCallerAsync(orderId, buyerId, httpContext.User.IsAdministrator());
        var notifications = await _notificationService.ListForOrderAsync(orderId, refreshFromProvider: true);

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
