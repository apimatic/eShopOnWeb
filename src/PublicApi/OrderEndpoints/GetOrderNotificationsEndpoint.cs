using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user, IShopperOrderService service) =>
            {
                return await HandleAsync(orderId, user, service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IShopperOrderService service)
        => HandleAsync(orderId, null!, service);

    private async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IShopperOrderService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.GetForBuyerAsync(orderId, buyerId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetForOrderAsync(orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        });
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = [];
}
