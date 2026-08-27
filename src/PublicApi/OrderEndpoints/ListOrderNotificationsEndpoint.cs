using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for one of the signed-in shopper's orders and what became of
/// each message, refreshed from the provider.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext, IRepository<Order>>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, httpContext, orderRepository);
            })
            .Produces<OrderNotificationDto[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext, IRepository<Order> orderRepository)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId, httpContext.RequestAborted);
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var spec = new NotificationsByOrderSpecification(orderId);
        var notifications = await _notificationRepository.ListAsync(spec, httpContext.RequestAborted);
        await _notificationService.RefreshStatusesAsync(notifications, httpContext.RequestAborted);

        return Results.Ok(notifications.Select(NotificationDtoMapper.ToDto).ToArray());
    }
}
