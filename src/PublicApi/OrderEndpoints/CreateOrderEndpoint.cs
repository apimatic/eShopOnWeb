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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS
/// (if they have a contact number on file). A notification failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private static readonly Address DefaultShipTo = new("1 eShop Way", "Redmond", "WA", "USA", "98052");

    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository,
                IRepository<CatalogItem> catalogItemRepository, IOrderNotificationService notificationService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderRepository, catalogItemRepository, notificationService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IOrderNotificationService notificationService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one item is required." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Item quantities must be positive." });
        }

        var catalogItems = await catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        var missing = request.Items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item ids: {string.Join(", ", missing)}" });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var shipTo = request.ShipTo is null
            ? DefaultShipTo
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = new Order(request.BuyerId, shipTo, orderItems);
        await orderRepository.AddAsync(order);

        await notificationService.NotifyOrderPlacedAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.OrderDate = order.OrderDate;
        response.Total = order.Total();
        response.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipTo { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
