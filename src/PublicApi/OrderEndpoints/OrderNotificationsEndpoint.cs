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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; }
    public string BuyerId { get; }

    public OrderNotificationsRequest(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }

    /// <summary>Each entry carries its own notificationId — the identifier the operator endpoints act on.</summary>
    public IReadOnlyList<NotificationView> Notifications { get; set; } = new List<NotificationView>();
}

/// <summary>
/// What was sent for one of the caller's orders and what became of each message (statuses refreshed
/// from the provider). Scoped to the caller — one shopper can never see another's order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await HandleAsync(new OrderNotificationsRequest(orderId, user.GetBuyerId()), service, cancellationToken))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var result = await service.GetOrderNotificationsAsync(request.OrderId, request.BuyerId, cancellationToken);
        if (!result.Found)
            return Results.NotFound();

        return Results.Ok(new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = result.Notifications
        });
    }
}
