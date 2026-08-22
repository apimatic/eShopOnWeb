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

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderNotificationService service, HttpContext httpContext) =>
            {
                return await HandleAsync(service, httpContext);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService orderNotificationService)
        => HandleAsync(orderNotificationService, null!);

    private Task<IResult> HandleAsync(IOrderNotificationService service, HttpContext httpContext)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var buyerId = httpContext.User.RequireBuyerId();
            var orders = await service.GetMyOrdersAsync(buyerId);
            var response = new GetMyOrdersResponse
            {
                Orders = orders.Select(o => new ShopperOrderDto
                {
                    OrderId = o.Order.Id,
                    Status = o.Order.Status.ToString(),
                    OrderDate = o.Order.OrderDate,
                    Total = o.Order.Total(),
                    Notifications = o.Notifications.Select(NotificationDto.From).ToList()
                }).ToList()
            };
            return Results.Ok(response);
        });
    }
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, service, httpContext);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderNotificationService orderNotificationService)
        => HandleAsync(orderId, orderNotificationService, null!);

    private Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, HttpContext httpContext)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var buyerId = httpContext.User.RequireBuyerId();
            var notifications = await service.GetOrderNotificationsAsync(buyerId, orderId);
            return Results.Ok(new GetOrderNotificationsResponse
            {
                OrderId = orderId,
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        });
    }
}

public class GetMyOrdersResponse
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

public class GetOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            SourceNotificationId = notification.SourceNotificationId
        };
    }
}
