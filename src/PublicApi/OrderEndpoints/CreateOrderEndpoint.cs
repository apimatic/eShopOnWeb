using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = "-";
    public string City { get; set; } = "-";
    public string State { get; set; } = "-";
    public string Country { get; set; } = "-";
    public string ZipCode { get; set; } = "-";
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<int> NotificationIds { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS
/// (when they have a contact number on file). A notification failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, CancellationToken>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly NotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        NotificationService notifications,
        IHttpContextAccessor httpContextAccessor)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, CancellationToken ct) =>
            {
                return await HandleAsync(request, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order needs at least one item." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Quantities must be positive." });
        }

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await _catalogItems.GetByIdAsync(item.CatalogItemId, ct);
            if (catalogItem is null)
            {
                return Results.BadRequest(new { message = $"Catalog item {item.CatalogItemId} does not exist." });
            }
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                item.Quantity));
        }

        var shipTo = request.ShipToAddress is null
            ? new Address("-", "-", "-", "-", "-")
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        await _orders.AddAsync(order, ct);

        var notifications = await _notifications.NotifyAsync(order, NotificationKind.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Total: {order.Total():0.00} USD. Thank you for shopping with us!", ct);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            NotificationIds = notifications.Select(n => n.Id).ToList()
        });
    }
}
