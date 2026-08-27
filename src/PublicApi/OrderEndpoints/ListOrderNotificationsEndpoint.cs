using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// What was sent for an order, and what became of each message. Shoppers see only their
/// own orders; operators (administrators) see any order. Delivery outcomes are refreshed
/// from the provider on a best-effort basis.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository,
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
                return await HandleAsync(new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    CallerId = user.Identity?.Name ?? string.Empty,
                    CallerIsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                });
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || (!request.CallerIsAdministrator && order.BuyerId != request.CallerId))
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(request.OrderId));

        foreach (var notification in notifications)
        {
            await _notificationService.RefreshStatusAsync(notification);
        }

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString()
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.FromEntity));
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool CallerIsAdministrator { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}
