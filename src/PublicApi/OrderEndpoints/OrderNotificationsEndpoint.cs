using System.Collections.Generic;
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

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public OrderNotificationsRequest(int orderId) => OrderId = orderId;
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. Each entry carries its own notificationId (what the operator endpoints act on). The
/// current delivery outcome is refreshed from the provider. Shopper-scoped: the caller must own the order.
/// </summary>
public class OrderNotificationsEndpoint : ApiEndpointBase,
    IEndpoint<IResult, OrderNotificationsRequest, IApiOrderService, INotificationService>
{
    public OrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IApiOrderService orderService, INotificationService notificationService) =>
                await HandleAsync(new OrderNotificationsRequest(orderId), orderService, notificationService))
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IApiOrderService orderService, INotificationService notificationService)
    {
        var buyerId = CallerId;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        // Only the order's owner may see its notifications.
        var order = await orderService.GetOrderForBuyerAsync(request.OrderId, buyerId, Aborted);
        if (order is null)
            return Results.NotFound();

        var notifications = await notificationService.GetOrderNotificationsAsync(request.OrderId, refreshFromProvider: true, Aborted);
        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
