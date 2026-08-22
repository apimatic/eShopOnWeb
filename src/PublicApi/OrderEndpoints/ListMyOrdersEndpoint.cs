using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IShopperOrderService orderService) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderService.ListForBuyerAsync(buyerId);
                var notifications = await _notificationService.ListForBuyerAsync(buyerId);
                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(order => new ShopperOrderDto
                    {
                        OrderId = order.Id,
                        Status = order.Status.ToString(),
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Notifications = byOrder.TryGetValue(order.Id, out var items)
                            ? items.Select(NotificationDto.From).ToList()
                            : new List<NotificationDto>()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderService orderService)
    {
        return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? OriginalKind { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            OriginalKind = notification.OriginalKind?.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ErrorCode = notification.ErrorCode,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.ContentRedacted ? null : notification.Body,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor
        };
    }
}
