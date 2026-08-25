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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _catalogItems;

    public CreateOrderEndpoint(IRepository<CatalogItem> catalogItems)
    {
        _catalogItems = catalogItems;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IRepository<Order> orderRepo, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<CreateOrderResponse>(201)
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepo)
    {
        if (request.Items == null || request.Items.Count == 0)
            return Results.BadRequest("At least one item is required.");

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await _catalogItems.GetByIdAsync(item.CatalogItemId);
            if (catalogItem is null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");
            if (item.Quantity <= 0)
                return Results.BadRequest($"Quantity for item {item.CatalogItemId} must be positive.");

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                item.Quantity));
        }

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
