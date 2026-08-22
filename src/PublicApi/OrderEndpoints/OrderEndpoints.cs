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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderFulfillmentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IOrderFulfillmentService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderFulfillmentService orderService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        var lines = request.Items.Select(i => new OrderLineRequest
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        var order = await orderService.PlaceOrderAsync(buyerId, lines);
        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public DispatchOrderRequest(int orderId) => OrderId = orderId;
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderFulfillmentService orderService) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), orderService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IOrderFulfillmentService orderService)
    {
        var order = await orderService.DispatchAsync(request.OrderId);
        return Results.Ok(new OrderActionResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderFulfillmentService orderService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderFulfillmentService orderService)
    {
        var order = await orderService.CancelAsync(request.OrderId);
        return Results.Ok(new OrderActionResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<BuyerOrderDto> Orders { get; set; } = new();
}

public class BuyerOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<BuyerOrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class BuyerOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderFulfillmentService>
{
    private readonly IReadRepository<OrderNotification> _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IReadRepository<OrderNotification> notifications, IHttpContextAccessor httpContextAccessor)
    {
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderFulfillmentService orderService) =>
            {
                return await HandleAsync(orderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderFulfillmentService orderService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        var orders = await orderService.ListBuyerOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse();

        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpec(order.Id));
            response.Orders.Add(MapOrder(order, notifications));
        }

        return Results.Ok(response);
    }

    internal static BuyerOrderDto MapOrder(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        return new BuyerOrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new BuyerOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Notifications = notifications.Select(MapNotification).ToList()
        };
    }

    internal static OrderNotificationDto MapNotification(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        Status = notification.ProviderStatus,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor
    };
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public ListOrderNotificationsRequest(int orderId) => OrderId = orderId;
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderFulfillmentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderFulfillmentService orderService) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), orderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderFulfillmentService orderService)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!);
        var notifications = await orderService.ListOrderNotificationsAsync(buyerId, request.OrderId);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(ListMyOrdersEndpoint.MapNotification).ToList()
        };

        return Results.Ok(response);
    }
}
