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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Populated from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool CallerIsAdmin { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}

/// <summary>
/// What was sent for an order, and what became of each message. Shoppers see
/// only their own orders; operators (Administrators) may see any order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IReadRepository<Order>>
{
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public OrderNotificationsEndpoint(
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    CallerId = user.Identity!.Name!,
                    CallerIsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                }, orderRepository);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IReadRepository<Order> orderRepository)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // Not-found and not-owned are indistinguishable for shoppers.
        if (order is null || (!request.CallerIsAdmin && order.BuyerId != request.CallerId))
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsSpecification(order.Id));

        // No callback URL exists, so delivery outcomes are pulled from the provider.
        await _notificationService.RefreshStatusesAsync(notifications);

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}
