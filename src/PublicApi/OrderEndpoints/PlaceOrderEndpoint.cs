using System;
using System.Collections.Generic;
using System.Security.Claims;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PlaceOrderRequest(List<PlaceOrderItemRequest> Items);
public record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public record PlaceOrderResponse(int OrderId, decimal Total);

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _catalogRepo;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IRepository<CatalogItem> catalogRepo, IHttpContextAccessor httpContextAccessor)
    {
        _catalogRepo = catalogRepo;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(request, orderRepo);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<Order> orderRepo)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var buyerId = user?.FindFirstValue(ClaimTypes.Email)
                   ?? user?.FindFirstValue("sub")
                   ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is not { Count: > 0 })
            return Results.BadRequest("At least one item is required.");

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                return Results.BadRequest($"Quantity must be positive for item {item.CatalogItemId}.");

            var catalogItem = await _catalogRepo.GetByIdAsync(item.CatalogItemId, ct);
            if (catalogItem == null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                item.Quantity));
        }

        var order = new Order(buyerId,
            new Address("Default St", "Seattle", "WA", "US", "98101"),
            orderItems);

        order = await orderRepo.AddAsync(order, ct);
        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse(order.Id, order.Total()));
    }
}
