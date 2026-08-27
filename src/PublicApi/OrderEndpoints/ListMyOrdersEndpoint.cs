using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                var services = httpContext.RequestServices;
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = httpContext.User.Identity?.Name },
                    services.GetRequiredService<IRepository<Order>>(),
                    services.GetRequiredService<IRepository<OrderNotification>>(),
                    services.GetRequiredService<IOrderNotificationService>());
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.BadRequest(response);
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));

        var allNotifications = new List<OrderNotification>();
        foreach (var order in orders)
        {
            allNotifications.AddRange(await notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id)));
        }

        // No callback URL exists for the provider to reach us, so fresh outcomes come from asking it.
        await notificationService.RefreshStatusesAsync(allNotifications);

        response.Orders = orders.Select(order => new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = allNotifications
                .Where(n => n.OrderId == order.Id)
                .Select(OrderNotificationDto.FromEntity)
                .ToList()
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}
