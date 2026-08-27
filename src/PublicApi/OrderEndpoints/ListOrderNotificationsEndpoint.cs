using System;
using System.Linq;
using System.Security.Claims;
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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for an order and what became of each message. Shoppers can only see
/// their own orders; administrators can see any order. Delivery outcomes are refreshed from
/// the provider on read (best-effort).
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal, IRepository<OrderNotification>>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly ISmsService _smsService;
    private readonly ILogger<ListOrderNotificationsEndpoint> _logger;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository, ISmsService smsService, ILogger<ListOrderNotificationsEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), user, notificationRepository);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal user, IRepository<OrderNotification> notificationRepository)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != user.Identity!.Name)
        {
            return Results.Forbid();
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(request.OrderId));

        foreach (var notification in notifications.Where(n => n.ProviderMessageSid is not null))
        {
            try
            {
                var providerState = await _smsService.GetMessageAsync(notification.ProviderMessageSid!);
                if (providerState is not null && providerState.Status != notification.Status)
                {
                    notification.UpdateStatus(providerState.Status, providerState.ErrorCode);
                    await notificationRepository.UpdateAsync(notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }

        return Results.Ok(new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDtoMapper.Map).ToList()
        });
    }
}
