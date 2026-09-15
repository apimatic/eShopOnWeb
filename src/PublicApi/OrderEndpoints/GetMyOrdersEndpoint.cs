using System;
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

public class GetMyOrdersRequest : BaseRequest
{
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderNotificationSummaryDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationSummaryDto> Notifications { get; set; } = new();
}

public class GetMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), orderRepository);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IRepository<Order> orderRepository)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetRequiredUserName();
        var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId));
        var notifications = await _notifications.ListForBuyerOrdersAsync(buyerId);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new GetMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = byOrder.TryGetValue(order.Id, out var list)
                    ? list.Select(ToSummary).ToList()
                    : new List<OrderNotificationSummaryDto>()
            };
            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }

    private static OrderNotificationSummaryDto ToSummary(NotificationView n) => new()
    {
        NotificationId = n.NotificationId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor
    };
}
