using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ResentFromNotificationId { get; set; }
    public string? SendFailure { get; set; }
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<ShopperOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ShopperOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(new EmptyRequest(), httpContext, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(EmptyRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        var buyerId = httpContext.BuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.ListMyOrdersAsync(buyerId, httpContext.RequestAborted);
        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(MapOrder).ToList()
        };
        return Results.Ok(response);
    }

    internal static ShopperOrderDto MapOrder(ShopperOrderView order)
    {
        return new ShopperOrderDto
        {
            OrderId = order.OrderId,
            Status = order.Status,
            OrderDate = order.OrderDate,
            Total = order.Total,
            Items = order.Items.Select(i => new ShopperOrderItemDto
            {
                CatalogItemId = i.CatalogItemId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = order.Notifications.Select(MapNotification).ToList()
        };
    }

    internal static NotificationDto MapNotification(NotificationView n)
    {
        return new NotificationDto
        {
            NotificationId = n.NotificationId,
            Kind = n.Kind.ToString(),
            Body = n.Body,
            ProviderSid = n.ProviderSid,
            ProviderStatus = n.ProviderStatus,
            ErrorCode = n.ErrorCode,
            ErrorMessage = n.ErrorMessage,
            ContentRedacted = n.ContentRedacted,
            CreatedAt = n.CreatedAt,
            ScheduledFor = n.ScheduledFor,
            ResentFromNotificationId = n.ResentFromNotificationId,
            SendFailure = n.SendFailure
        };
    }
}
