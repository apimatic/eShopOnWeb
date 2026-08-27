using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;

    public ListMyOrdersEndpoint(IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = user.Identity?.Name ?? string.Empty });
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));

        var response = new ListMyOrdersResponse(request.CorrelationId());
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notificationRepository.ListAsync(
                new OrderNotificationsByOrderSpecification(order.Id));

            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
            });
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new List<MyOrderItemDto>();
    public List<NotificationDto> Notifications { get; set; } = new List<NotificationDto>();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new List<MyOrderDto>();
}
