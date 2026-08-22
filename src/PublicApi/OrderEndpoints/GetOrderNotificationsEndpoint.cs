using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationQueryService _notificationQueries;

    public GetOrderNotificationsEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationQueryService notificationQueries)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationQueries = notificationQueries;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IShopperOrderService service)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.RequireBuyerId()!;
        var order = await service.GetBuyerOrderAsync(orderId, buyerId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationQueries.GetForOrderAsync(orderId);
        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        });
    }
}
