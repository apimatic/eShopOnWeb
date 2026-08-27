using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public int? ErrorCode { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public System.DateTimeOffset? ScheduledAt { get; set; }
    public int? ParentNotificationId { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest { OrderId = orderId }, service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService service)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = httpContext.RequireBuyerId();
        var notifications = await service.ListNotificationsAsync(request.OrderId, buyerId, httpContext.IsAdministrator());
        var response = new ListOrderNotificationsResponse();
        response.Notifications.AddRange(notifications.Select(OrderNotificationMapper.ToDto));
        return Results.Ok(response);
    }
}

internal static class OrderNotificationMapper
{
    public static OrderNotificationDto ToDto(OrderNotificationView notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.NotificationId,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid,
            Body = notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ErrorCode = notification.ErrorCode,
            CreatedAt = notification.CreatedAt,
            ScheduledAt = notification.ScheduledAt,
            ParentNotificationId = notification.ParentNotificationId
        };
    }

    public static MyOrderDto ToDto(ShopperOrderView order)
    {
        return new MyOrderDto
        {
            OrderId = order.OrderId,
            Status = order.Status,
            OrderDate = order.OrderDate,
            Total = order.Total,
            Items = order.Items.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.CatalogItemId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = order.Notifications.Select(ToDto).ToList()
        };
    }
}
