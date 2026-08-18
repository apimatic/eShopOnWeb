using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to
/// (their delivery outcomes are refreshed from the provider at read time).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IOrderNotificationService notificationService, IHttpContextAccessor httpContextAccessor)
    {
        _notificationService = notificationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository) =>
                await HandleAsync(orderRepository))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository)
    {
        var ownerId = _httpContextAccessor.GetCallerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId));
        var notifications = await _notificationService.RefreshOwnerNotificationsAsync(ownerId);
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.CreatedAt).ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new MyOrderDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Total = o.Total(),
                    Notifications = notificationsByOrder.TryGetValue(o.Id, out var list)
                        ? list.Select(NotificationDto.FromEntity).ToList()
                        : new()
                })
                .ToList()
        };

        return Results.Ok(response);
    }
}
