using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService orderService) =>
            {
                return await HandleAsync(orderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IShopperOrderService orderService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        var ct = _httpContextAccessor.HttpContext!.RequestAborted;
        var orders = await orderService.ListMineAsync(buyerId, ct);
        var notifications = await _notifications.ListForBuyerOrdersAsync(buyerId, orders.Select(o => o.Id).ToList(), ct);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(orders.Select(order => new MyOrderDto
        {
            OrderId = order.Id,
            FulfillmentStatus = order.FulfillmentStatus,
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Notifications = byOrder.TryGetValue(order.Id, out var notes)
                ? notes.Select(ToDto).ToList()
                : new()
        }));
        return Results.Ok(response);
    }

    internal static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind,
            Status = notification.Status,
            ProviderSid = notification.ProviderSid,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt
        };
    }
}
