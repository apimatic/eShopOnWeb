using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
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
using BlazorSharedAuth = BlazorShared.Authorization.Constants;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

// ----- DTOs -----

public record OrderLineDto(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public record PlaceOrderResponse(int OrderId);

public record NotificationDto(
    int NotificationId,
    string Kind,
    string DeliveryStatus,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? SentAt,
    bool IsScheduled,
    bool ContentRedacted);

public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);

public record MyOrderDto(int OrderId, string Status, DateTimeOffset OrderDate, decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(OrderNotification n) => new(
        n.Id, n.Kind.ToString(), n.DeliveryStatus.ToString(), n.ProviderStatusRaw,
        n.ProviderMessageSid, n.ProviderErrorCode, n.ProviderErrorMessage, n.ProviderSentAt,
        n.IsScheduled, n.ContentRedacted);
}

// ----- POST /api/orders -----

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = owner;
                return await Execute(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(PlaceOrderRequest request, IOrderNotificationService service,
        CancellationToken ct)
    {
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        // Address is outside this feature's scope but the Order model requires one; default when not supplied.
        var a = request.ShipToAddress;
        var address = new Address(
            string.IsNullOrWhiteSpace(a?.Street) ? "N/A" : a!.Street,
            string.IsNullOrWhiteSpace(a?.City) ? "N/A" : a!.City,
            string.IsNullOrWhiteSpace(a?.State) ? "N/A" : a!.State,
            string.IsNullOrWhiteSpace(a?.Country) ? "N/A" : a!.Country,
            string.IsNullOrWhiteSpace(a?.ZipCode) ? "00000" : a!.ZipCode);

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address, ct);
        return result.Outcome switch
        {
            PlaceOrderOutcome.Placed => Results.Created($"api/orders/{result.OrderId}",
                new PlaceOrderResponse(result.OrderId!.Value)),
            _ => Results.BadRequest(new { message = result.Message })
        };
    }
}

// ----- POST /api/orders/{orderId}/dispatch (operator) -----

public class OrderDispatchEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorSharedAuth.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
                await Execute(new OrderActionRequest { OrderId = orderId }, service, ct, dispatch: true))
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderActionRequest request, IOrderNotificationService service)
        => Execute(request, service, CancellationToken.None, dispatch: true);

    internal static async Task<IResult> Execute(OrderActionRequest request, IOrderNotificationService service,
        CancellationToken ct, bool dispatch)
    {
        var outcome = dispatch
            ? await service.DispatchAsync(request.OrderId, ct)
            : await service.CancelAsync(request.OrderId, ct);
        var stateName = dispatch ? "Dispatched" : "Cancelled";
        return outcome switch
        {
            OrderActionOutcome.Done => Results.Ok(new { orderId = request.OrderId, status = stateName }),
            OrderActionOutcome.NoOp => Results.Conflict(new
            {
                orderId = request.OrderId,
                message = dispatch
                    ? "Order is not in a state that can be dispatched."
                    : "Order is already cancelled."
            }),
            _ => Results.NotFound()
        };
    }
}

// ----- POST /api/orders/{orderId}/cancel (operator) -----

public class OrderCancelEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorSharedAuth.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken ct) =>
                await OrderDispatchEndpoint.Execute(new OrderActionRequest { OrderId = orderId }, service, ct, dispatch: false))
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderActionRequest request, IOrderNotificationService service)
        => OrderDispatchEndpoint.Execute(request, service, CancellationToken.None, dispatch: false);
}

public class OrderActionRequest
{
    public int OrderId { get; set; }
}

// ----- GET /api/orders/{orderId}/notifications (shopper-scoped) -----

public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                return await Execute(new OrderNotificationsRequest { OwnerId = owner, OrderId = orderId }, service, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(OrderNotificationsRequest request,
        IOrderNotificationService service, CancellationToken ct)
    {
        var notifications = await service.GetOrderNotificationsAsync(request.OwnerId, request.OrderId,
            refreshFromProvider: true, ct);
        if (notifications is null)
        {
            return Results.NotFound();
        }
        var dtos = notifications.Select(NotificationDtoMapper.ToDto).ToList();
        return Results.Ok(new OrderNotificationsResponse(request.OrderId, dtos));
    }
}

public class OrderNotificationsRequest
{
    public int OrderId { get; set; }

    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

// ----- GET /api/my-orders (shopper-scoped) -----

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var owner = http.GetUserName();
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                return await Execute(new MyOrdersRequest { OwnerId = owner }, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
        => Execute(request, service, CancellationToken.None);

    private static async Task<IResult> Execute(MyOrdersRequest request, IOrderNotificationService service,
        CancellationToken ct)
    {
        var views = await service.GetMyOrdersAsync(request.OwnerId, ct);
        var dtos = views.Select(v => new MyOrderDto(
            v.Order.Id, v.Order.Status.ToString(), v.Order.OrderDate, v.Order.Total(),
            v.Notifications.Select(NotificationDtoMapper.ToDto).ToList())).ToList();
        return Results.Ok(new MyOrdersResponse(dtos));
    }
}

public class MyOrdersRequest
{
    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}
