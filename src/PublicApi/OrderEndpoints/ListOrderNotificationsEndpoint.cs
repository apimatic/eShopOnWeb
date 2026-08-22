using System;
using System.Linq;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, orderRepository);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.GetRequiredBuyerId();
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDtoMapper.From).ToList()
        });
    }
}
