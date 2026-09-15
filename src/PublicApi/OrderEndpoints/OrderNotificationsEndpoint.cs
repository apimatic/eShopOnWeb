using System;
using System.Collections.Generic;
using System.Linq;
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
/// Returns what was sent for one of the caller's own orders and what became of each message. Each
/// entry carries its own notificationId — what the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var request = new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    CallerId = CallerIdentity.Get(httpContext) ?? string.Empty
                };
                return await HandleAsync(request, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var notifications = await service.GetOwnedOrderNotificationsAsync(request.OrderId, request.CallerId);
        if (notifications is null)
            return Results.NotFound();

        var response = new OrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.From));
        return Results.Ok(response);
    }
}

public class OrderNotificationsRequest : AuthenticatedRequest
{
    public int OrderId { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderNotificationsResponse()
    {
    }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
