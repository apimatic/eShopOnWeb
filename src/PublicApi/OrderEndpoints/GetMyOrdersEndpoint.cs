using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider as part of the read.
/// </summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public GetMyOrdersEndpoint(IReadRepository<Order> orderRepository, IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(user, ct))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _orderNotificationService.GetBuyerNotificationsAsync(buyerId, ct);
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(NotificationDto.FromEntity).ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = notificationsByOrder.TryGetValue(o.Id, out var list) ? list : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
