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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for one of the signed-in shopper's orders, and what
/// became of each message.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal, IRepository<Order>>
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
            (int orderId, ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), user, orderRepository);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository)
    {
        var response = new ListOrderNotificationsResponse(request.CorrelationId());
        var buyerId = user.Identity!.Name!;

        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // A shopper must never see another shopper's order.
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetOrderNotificationsAsync(order.Id);
        response.OrderId = order.Id;
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.FromEntity));

        return Results.Ok(response);
    }
}
