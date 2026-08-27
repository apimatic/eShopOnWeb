using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Shows what was sent for an order and what became of each message. Outcomes are
/// refreshed from the provider on read. Visible to the order's owner and to operators.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public ListOrderNotificationsEndpoint(
        IRepository<Order> orderRepository,
        IRepository<Notification> notificationRepository,
        IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(orderId);
        var isAdmin = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (order is null || (order.BuyerId != buyerId && !isAdmin))
        {
            return Results.NotFound();
        }

        await _orderNotificationService.RefreshOrderNotificationsAsync(orderId);

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}
