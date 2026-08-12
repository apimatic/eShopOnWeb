using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderShippingAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address. When omitted a placeholder is used — the flow under test is notifications, not fulfilment.</summary>
    public PlaceOrderShippingAddress? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids + quantities, reusing the app's
/// existing Order/OrderItem model. The shopper is then told their order was placed. The buyer's identity
/// comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _http;

    public PlaceOrderEndpoint(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications,
        IHttpContextAccessor http)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request) => await HandleAsync(request))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request)
    {
        var ct = _http.HttpContext!.RequestAborted;
        var buyerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });
        }

        var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(itemIds), ct);
        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = request.Items.Select(requested =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var order = new Order(buyerId, BuildAddress(request.ShipToAddress), orderItems);
        await _orders.AddAsync(order, ct);

        // Tell the shopper their order was placed. Messaging is best-effort and never fails this request.
        await _notifications.NotifyOrderPlacedAsync(order, ct);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(PlaceOrderShippingAddress? address)
    {
        if (address == null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "N/A" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City,
            address.State ?? string.Empty,
            string.IsNullOrWhiteSpace(address.Country) ? "N/A" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode);
    }
}
