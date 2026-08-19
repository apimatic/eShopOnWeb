using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? CallerId { get; set; }
    public bool IsOperator { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = Array.Empty<NotificationView>();
}

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. A shopper sees only their own order; an operator may see any order (needed to act on
/// the notification identifiers the operator endpoints take).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var isOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    CallerId = user.Identity?.Name,
                    IsOperator = isOperator
                }, service);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        // Operators may view any order; shoppers are scoped to their own.
        var ownerScope = request.IsOperator ? null : request.CallerId;
        var notifications = await service.GetOrderNotificationsAsync(request.OrderId, ownerScope, CancellationToken.None);
        if (notifications is null)
            return Results.NotFound();

        return Results.Ok(new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications
        });
    }
}
