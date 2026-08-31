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
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items, reusing the app's existing order/order-item model. The
/// order is owned by the authenticated caller. This is the entry point that makes the whole
/// invoicing flow drivable through PublicApi alone.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderRepository, itemRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every item quantity must be greater than zero.");
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(ids));

        var orderItems = new System.Collections.Generic.List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                return Results.BadRequest($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = BuildAddress(request.ShipToAddress);
        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepository.AddAsync(order);

        response.OrderId = order.Id;
        response.Total = order.Total();
        response.Items = order.OrderItems.Select(oi => new CreateOrderResponseItem
        {
            CatalogItemId = oi.ItemOrdered.CatalogItemId,
            ProductName = oi.ItemOrdered.ProductName,
            UnitPrice = oi.UnitPrice,
            Units = oi.Units
        }).ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(CreateOrderAddress? address)
    {
        if (address is null)
        {
            // Invoicing does not use the shipping address; a placeholder keeps the order model valid.
            return new Address("123 Main St", "Redmond", "WA", "USA", "98052");
        }

        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "123 Main St" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "Redmond" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "WA" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "98052" : address.ZipCode);
    }
}
