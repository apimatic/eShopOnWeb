using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for one of the signed-in shopper's orders and what became of each message,
/// refreshing live delivery outcomes from the provider. Scoped to the caller's own orders.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IReadRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository) =>
                await HandleAsync(orderId, orderRepository))
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IReadRepository<Order> orderRepository)
    {
        var ownerId = _httpContextAccessor.GetOwnerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();

        // A shopper only ever sees their own order's notifications; a mismatch reads as not-found.
        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != ownerId)
        {
            return Results.NotFound();
        }

        var notificationService = _httpContextAccessor.RequestService<IOrderNotificationService>();
        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, refreshFromProvider: true, ct);

        return Results.Ok(new GetOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => n.ToDto()).ToList()
        });
    }
}
