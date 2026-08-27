using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order, and what became of each message. Shoppers see
/// their own orders; operators (administrators) see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal>
{
    private static readonly HashSet<string> NonTerminalStatuses = new()
    {
        "accepted", "queued", "sending", "sent", "scheduled"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));
        foreach (var notification in notifications.Where(n => NonTerminalStatuses.Contains(n.Status)))
        {
            await _notificationService.RefreshStatusAsync(notification);
        }

        var response = new ListOrderNotificationsResponse { OrderId = order.Id };
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.FromEntity));
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
