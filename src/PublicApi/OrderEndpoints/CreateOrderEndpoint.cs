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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<CatalogItem>>
{
    private readonly IRepository<Order> _orderRepository;

    public CreateOrderEndpoint(IRepository<Order> orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IRepository<CatalogItem> catalogRepository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(request with { BuyerId = buyerId }, catalogRepository);
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<CatalogItem> catalogRepository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (request.Items == null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "Order must contain at least one item." });

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).ToList();
        var spec = new ApplicationCore.Specifications.CatalogItemsSpecification(catalogItemIds.ToArray());
        var catalogItems = await catalogRepository.ListAsync(spec);

        var orderItems = new List<OrderItem>();
        foreach (var lineItem in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
            if (catalogItem == null)
                return Results.BadRequest(new { error = $"Catalog item {lineItem.CatalogItemId} not found." });
            if (lineItem.Quantity <= 0)
                return Results.BadRequest(new { error = $"Quantity must be positive for item {lineItem.CatalogItemId}." });

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                lineItem.Quantity));
        }

        var address = new Address(
            request.ShipToAddress.Street,
            request.ShipToAddress.City,
            request.ShipToAddress.State,
            request.ShipToAddress.Country,
            request.ShipToAddress.ZipCode);

        var order = new Order(request.BuyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse { OrderId = order.Id });
    }
}
