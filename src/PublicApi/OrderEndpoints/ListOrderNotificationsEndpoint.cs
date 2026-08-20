using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IRepository<Order>>
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
            (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(
                    new ListOrderNotificationsRequest(orderId, httpContext.GetRequiredBuyerId(), httpContext.IsAdministrator()),
                    orderRepository);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || (!request.IsAdministrator && !order.BelongsTo(request.BuyerId)))
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(order.Id);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDtoMapper.ToDto).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId, string buyerId, bool isAdministrator)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        IsAdministrator = isAdministrator;
    }

    public int OrderId { get; }
    internal string BuyerId { get; }
    internal bool IsAdministrator { get; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<OrderNotificationDto> Notifications { get; set; } = new();
}
