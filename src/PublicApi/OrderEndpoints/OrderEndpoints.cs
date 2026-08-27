using System;
using System.Collections.Generic;
using System.Linq;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                var buyerId = EndpointIdentity.GetRequiredBuyerId(httpContext);
                request.BuyerId = buyerId;
                return await HandleAsync(request, shopperOrderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService shopperOrderService)
    {
        var items = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CatalogOrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await shopperOrderService.PlaceAsync(request.BuyerId, items);
        var response = new CreateOrderResponse { OrderId = order.Id };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                var buyerId = EndpointIdentity.GetRequiredBuyerId(httpContext);
                return await HandleAsync(buyerId, shopperOrderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IShopperOrderService shopperOrderService)
    {
        var summaries = await shopperOrderService.ListMineAsync(buyerId);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = summaries.Select(OrderResponseMapper.ToDto).ToList()
        });
    }
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                var request = new ListOrderNotificationsRequest(
                    orderId,
                    EndpointIdentity.GetRequiredBuyerId(httpContext),
                    EndpointIdentity.IsAdministrator(httpContext));
                return await HandleAsync(request, shopperOrderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService shopperOrderService)
    {
        var notifications = await shopperOrderService.ListNotificationsAsync(
            request.OrderId,
            request.BuyerId,
            request.IsAdministrator);

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(OrderResponseMapper.ToDto).ToList()
        });
    }
}

public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(orderId, operatorService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOperatorOrderNotificationService operatorService)
    {
        var order = await operatorService.DispatchAsync(orderId);
        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(orderId, operatorService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOperatorOrderNotificationService operatorService)
    {
        var order = await operatorService.CancelAsync(orderId);
        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

public class ListMyOrdersResponse
{
    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<ShopperOrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ShopperOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public record ListOrderNotificationsRequest(int OrderId, string BuyerId, bool IsAdministrator);

public class ListOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

internal static class OrderResponseMapper
{
    public static ShopperOrderDto ToDto(ShopperOrderSummary summary)
    {
        return new ShopperOrderDto
        {
            OrderId = summary.Order.Id,
            Status = summary.Order.Status.ToString(),
            OrderDate = summary.Order.OrderDate,
            Total = summary.Order.Total(),
            Items = summary.Order.OrderItems.Select(i => new ShopperOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Notifications = summary.Notifications.Select(ToDto).ToList()
        };
    }

    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentDisposed ? null : notification.Body,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ScheduledSendAt = notification.ScheduledSendAt,
            ContentDisposed = notification.ContentDisposed,
            CreatedAt = notification.CreatedAt
        };
    }
}
