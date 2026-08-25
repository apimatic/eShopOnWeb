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

public class CreateOrderRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest ShipToAddress { get; set; } = new();
}

public class OrderItemRequest
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<CatalogItem>>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IRepository<Order> orderRepository, IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IRepository<CatalogItem> catalogRepo) =>
            {
                request.BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, catalogRepo);
            })
            .Produces<object>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<CatalogItem> catalogRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (!request.Items.Any())
            return Results.BadRequest(new { error = "Order must contain at least one item." });

        var ids = request.Items.Select(i => i.CatalogItemId).ToArray();
        var spec = new CatalogItemsSpecification(ids);
        var catalogItems = await catalogRepo.ListAsync(spec);

        var orderItems = new List<OrderItem>();
        foreach (var itemReq in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == itemReq.CatalogItemId);
            if (catalogItem is null)
                return Results.BadRequest(new { error = $"Catalog item {itemReq.CatalogItemId} not found." });
            if (itemReq.Quantity <= 0)
                return Results.BadRequest(new { error = $"Quantity for item {itemReq.CatalogItemId} must be positive." });

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, itemReq.Quantity));
        }

        var addr = request.ShipToAddress;
        var address = new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode);
        var order = new Order(request.BuyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }
}
