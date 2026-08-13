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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public IReadOnlyList<NotificationSummary> Notifications { get; set; } = new List<NotificationSummary>();
}

/// <summary>
/// Returns what was sent for an order and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Visible to the order's owner and to
/// administrators; another shopper's order is treated as not found.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, ISmsNotificationService service) =>
                await HandleAsync(new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = user.GetBuyerId(),
                    IsAdministrator = user.IsAdministrator()
                }, service))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, ISmsNotificationService service)
    {
        var order = await service.GetOrderAsync(request.OrderId);
        // One shopper must never see another's order; an unowned/missing order is simply "not found".
        if (order is null || (!request.IsAdministrator && order.BuyerId != request.BuyerId))
            return Results.NotFound();

        var notifications = await service.GetOrderNotificationsAsync(request.OrderId);
        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationSummary.From).ToList()
        };
        return Results.Ok(response);
    }
}
