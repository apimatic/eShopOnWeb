using System;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the caller's own orders, and what became of each message. Each entry carries
/// its own notificationId (what the operator endpoints act on). Shopper-scoped: the caller must own the order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var ownerId = user.ShopperId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                return await ExecuteAsync(new OrderNotificationsRequest { OrderId = orderId, OwnerId = ownerId }, service, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(OrderNotificationsRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        // Ownership check: one shopper must never see another's order.
        var order = await service.GetOrderForOwnerAsync(request.OrderId, request.OwnerId, cts.Token);
        if (order is null)
            return Results.NotFound();

        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId, refreshFromProvider: true, cts.Token);
        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
