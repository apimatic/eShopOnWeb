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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;

    public GetMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderRepository, httpContext);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orderRepository)
    {
        return HandleAsync(orderRepository, null!);
    }

    private async Task<IResult> HandleAsync(IRepository<Order> orderRepository, HttpContext httpContext)
    {
        var buyerId = httpContext.User.RequireBuyerId();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationService.ListForBuyerAsync(buyerId);
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(NotificationDto.FromEntity).ToList());

        var response = new GetMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotifications)
                    ? orderNotifications
                    : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
