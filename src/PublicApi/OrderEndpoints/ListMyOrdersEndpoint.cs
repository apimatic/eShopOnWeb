using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each with its notifications and where they got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = user.Identity!.Name! }, orderRepository, notificationRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListMyOrdersRequest request,
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());

        var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(request.BuyerId));
        var notifications = await notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(request.BuyerId));
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        response.Orders = orders.Select(o => new MyOrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            Notifications = notificationsByOrder.TryGetValue(o.Id, out var orderNotifications)
                ? orderNotifications.Select(NotificationDto.FromEntity).ToList()
                : new List<NotificationDto>()
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? MessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        Body = n.Body,
        ContentRedacted = n.ContentRedacted,
        MessageSid = n.MessageSid,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor
    };
}
