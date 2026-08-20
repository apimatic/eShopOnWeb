using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
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
            (int orderId, ClaimsPrincipal user, IShopperOrderService orderService) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = user.GetBuyerId(),
                    IsAdministrator = user.IsAdministrator()
                }, orderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService orderService)
    {
        var order = await orderService.GetByIdAsync(request.OrderId);
        if (order is null || (!request.IsAdministrator && order.BuyerId != request.BuyerId))
        {
            throw new EntityNotFoundException("Order not found.");
        }

        var notifications = await _notificationService.ListForOrderAsync(order.Id);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
    internal bool IsAdministrator { get; set; }
}

public class ListOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
