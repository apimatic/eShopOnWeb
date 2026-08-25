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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _catalogRepo;

    public CreateOrderEndpoint(IRepository<CatalogItem> catalogRepo)
    {
        _catalogRepo = catalogRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepo) =>
            {
                request.BuyerId = user.Identity?.Name ?? "";
                return await HandleAsync(request, orderRepo);
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (request.Items == null || !request.Items.Any())
            return Results.BadRequest(new { error = "Order must contain at least one item." });

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).ToArray();
        var spec = new CatalogItemsSpecification(catalogItemIds);
        var catalogItems = await _catalogRepo.ListAsync(spec);

        var orderItems = new List<OrderItem>();
        foreach (var lineItem in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
            if (catalogItem == null)
                return Results.BadRequest(new { error = $"Catalog item {lineItem.CatalogItemId} not found." });
            if (lineItem.Quantity <= 0)
                return Results.BadRequest(new { error = $"Quantity for item {lineItem.CatalogItemId} must be positive." });

            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? "");
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, lineItem.Quantity));
        }

        var address = new Address(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);

        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId()) { OrderId = order.Id };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = "";
    public List<OrderLineItem> Items { get; set; } = new();
    public ShippingAddressDto ShippingAddress { get; set; } = new();
}

public class OrderLineItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
}
