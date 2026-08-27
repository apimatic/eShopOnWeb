using System;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the signed-in shopper's orders, and what
/// became of each message (refreshed from the provider).
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null || order.BuyerId != user.Identity!.Name)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var notifications = await _notificationService.GetOrderNotificationsAsync(orderId);

        var response = new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(ListMyOrdersEndpoint.MapNotification).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListOrderNotificationsResponse()
    {
    }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
