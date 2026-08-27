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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the signed-in shopper's orders, and what became of each
/// message. Delivery outcomes are refreshed from the provider best-effort on each read.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly INotificationGateway _gateway;
    private readonly IAppLogger<ListOrderNotificationsEndpoint> _logger;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        INotificationGateway gateway,
        IAppLogger<ListOrderNotificationsEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _logger = logger;
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
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(request.OrderId));

        foreach (var notification in notifications.Where(n => n.MessageSid is not null))
        {
            try
            {
                var current = await _gateway.GetMessageAsync(notification.MessageSid!);
                if (current.Status is not null && current.Status != notification.Status)
                {
                    notification.UpdateFromProvider(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification);
                }
            }
            catch (NotificationProviderException ex)
            {
                _logger.LogInformation($"Could not refresh status for notification {notification.Id}: {ex.Message}");
            }
        }

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications
                .Select(n => OrderNotificationDto.FromEntity(n, includeBody: true))
                .ToList()
        };
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
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) {}
    public ListOrderNotificationsResponse() {}

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
