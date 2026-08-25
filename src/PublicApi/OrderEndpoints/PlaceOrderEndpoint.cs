using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint
{
    private readonly IRepository<CatalogItem> _catalogRepo;
    private readonly IRepository<Order> _orderRepo;

    public PlaceOrderEndpoint(IRepository<CatalogItem> catalogRepo, IRepository<Order> orderRepo)
    {
        _catalogRepo = catalogRepo;
        _orderRepo = orderRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, PlaceOrderRequest request) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(request, buyerId, ctx.RequestAborted);
            })
            .Produces<PlaceOrderResponse>(201)
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, string buyerId, System.Threading.CancellationToken ct)
    {
        if (request.Items == null || request.Items.Count == 0)
            return Results.BadRequest("Order must contain at least one item.");

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await _catalogRepo.GetByIdAsync(item.CatalogItemId, ct);
            if (catalogItem == null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");
            if (item.Quantity <= 0)
                return Results.BadRequest($"Quantity for item {item.CatalogItemId} must be positive.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var shipTo = new Address(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orderRepo.AddAsync(order, ct);

        return Results.Created($"/api/orders/{order.Id}", new PlaceOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = order.PaymentStatus.ToString()
        });
    }
}

public class PlaceOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}
