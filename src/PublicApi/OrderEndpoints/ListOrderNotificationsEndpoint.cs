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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IShopOrderService shopOrderService) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = user.GetBuyerId(),
                    IsAdministrator = user.IsAdministrator()
                }, shopOrderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopOrderService shopOrderService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var order = await shopOrderService.GetOrderAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (!request.IsAdministrator && order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetForOrderAsync(request.OrderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public bool IsAdministrator { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
