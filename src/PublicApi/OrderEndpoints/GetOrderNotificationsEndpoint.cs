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
/// What was sent for an order, and what became of each message. Shoppers see their own
/// orders; administrators see any order.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IReadRepository<Order>, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, INotificationService notificationService) =>
            {
                var request = new GetOrderNotificationsRequest(orderId)
                {
                    CallerId = user.Identity?.Name ?? string.Empty,
                    CallerIsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                };
                return await HandleAsync(request, orderRepository, notificationService);
            })
            .Produces<GetOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IReadRepository<Order> orderRepository, INotificationService notificationService)
    {
        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || (!request.CallerIsAdministrator && order.BuyerId != request.CallerId))
        {
            // Not leaking whether the order exists for someone else's id.
            throw new EntityNotFoundException($"Order {request.OrderId} was not found.");
        }

        var response = new GetOrderNotificationsResponse(request.CorrelationId());

        var notifications = await notificationService.GetOrderNotificationsAsync(order.Id);

        response.OrderId = order.Id;
        response.Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList();

        return Results.Ok(response);
    }
}
