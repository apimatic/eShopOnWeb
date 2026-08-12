using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's own orders, each showing where its notifications got to.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly INotificationService _notificationService;

    public ListMyOrdersEndpoint(
        IReadRepository<Order> orderRepository,
        IRepository<Notification> notificationRepository,
        INotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http) =>
            {
                return await HandleAsync(http);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

        // Gather notifications for these orders and refresh their delivery outcomes from the provider,
        // so "where its notifications got to" reflects the provider's current view.
        var notificationsByOrder = new Dictionary<int, List<Notification>>();
        var allNotifications = new List<Notification>();
        foreach (var order in orders)
        {
            var list = (await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id))).ToList();
            notificationsByOrder[order.Id] = list;
            allNotifications.AddRange(list);
        }
        await _notificationService.RefreshStatusesAsync(allNotifications);

        var response = new ListMyOrdersResponse
        {
            Orders = orders
                .Select(o => MyOrderDto.From(o, notificationsByOrder[o.Id].Select(NotificationDto.From)))
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
