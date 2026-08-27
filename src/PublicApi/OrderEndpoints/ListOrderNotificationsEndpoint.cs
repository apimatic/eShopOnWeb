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
/// What was sent for an order and what became of each message. Shoppers see their own
/// orders; administrators can see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal claimsPrincipal) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), claimsPrincipal);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal claimsPrincipal)
    {
        var orderRepository = _orderRepository;
        var notificationService = _notificationService;
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var isAdmin = claimsPrincipal.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await notificationService.ListForOrderAsync(order.Id);

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationMapping.ToDto).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
