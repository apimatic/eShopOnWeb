using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order and what became of each message. Each entry carries its own
/// notificationId — that is what the operator endpoints act on. Visible to the order's owner, or to
/// an administrator (operator).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ISmsNotificationService service, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                var request = new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = buyerId,
                    IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                };
                return await HandleAsync(request, service);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, ISmsNotificationService service)
    {
        var owner = await service.GetOrderBuyerAsync(request.OrderId);
        if (owner is null)
            return Results.NotFound();

        // Shopper-scoped: only the order's owner sees it; an administrator (operator) may see any.
        if (!request.IsAdministrator && owner != request.BuyerId)
            return Results.NotFound();

        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId, refresh: true);
        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(n => n.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<SmsNotificationDto> Notifications { get; set; } = new();
}
